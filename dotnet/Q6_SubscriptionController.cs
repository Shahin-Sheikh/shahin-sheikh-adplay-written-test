using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdPlay.Api.Controllers
{
    // Q6. Prevent duplicate subscription under 1,000 concurrent requests.
    public class SubscriptionController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SubscriptionController> _logger;

        public SubscriptionController(AppDbContext context, ILogger<SubscriptionController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpPost("api/subscriptions")]
        public async Task<IActionResult> Subscribe([FromBody] SubscriptionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Mobile))
                return BadRequest(new { error = "Mobile number is required." });

            // Basic validation: mobile should be 10-15 digits (adjust per your region)
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.Mobile, @"^\d{10,15}$"))
                return BadRequest(new { error = "Mobile number must be 10-15 digits." });

            try
            {
                var rowsAffected = await _context.Users
                    .Where(x => x.Mobile == request.Mobile && !x.IsSubscribed)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(u => u.IsSubscribed, true)
                        .SetProperty(u => u.SubscribedAtUtc, DateTime.UtcNow));

                if (rowsAffected == 0)
                {
                    var exists = await _context.Users.AnyAsync(x => x.Mobile == request.Mobile);
                    if (!exists)
                    {
                        _logger.LogWarning("Subscription attempt for non-existent user: {Mobile}", request.Mobile);
                        return NotFound(new { error = "User not found." });
                    }

                    // Either already subscribed, or a concurrent request just won the race.
                    _logger.LogInformation("User already subscribed: {Mobile}", request.Mobile);
                    return Conflict(new { error = "User is already subscribed." });
                }

                _logger.LogInformation("User subscribed successfully: {Mobile}", request.Mobile);
                return Ok(new { message = "Subscribed successfully.", subscribedAt = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subscription failed for {Mobile}", request.Mobile);
                return StatusCode(500, new { error = "An unexpected error occurred." });
            }
        }
    }

    public class SubscriptionRequest
    {
        public string Mobile { get; set; } = default!;
    }
}
