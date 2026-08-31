using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdPlay.Api.Controllers
{

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

        [HttpPost("api/subscriptions/legacy")]
        public async Task<IActionResult> SubscribeLegacy([FromBody] SubscriptionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Mobile))
                return BadRequest(new { error = "Mobile number is required." });

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var user = await _context.Users
                    .FromSqlInterpolated($"SELECT * FROM Users WHERE Mobile = {request.Mobile} FOR UPDATE")
                    .FirstOrDefaultAsync();

                if (user == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound(new { error = "User not found." });
                }

                if (user.IsSubscribed)
                {
                    await transaction.RollbackAsync();
                    return Conflict(new { error = "User is already subscribed." });
                }

                user.IsSubscribed = true;
                user.SubscribedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = "Subscribed successfully." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
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
