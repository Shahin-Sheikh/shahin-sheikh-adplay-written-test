using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace AdPlay.Api.Payments
{
    // Q7. POST /api/payment
    // Requirements covered: idempotency key, retry mechanism, transaction management,
    // distributed lock, duplicate payment prevention, proper HTTP status codes.

    [ApiController]
    [Route("api/payment")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _paymentService;

        public PaymentController(IPaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        [HttpPost]
        public async Task<IActionResult> ProcessPayment(
            [FromBody] PaymentRequest request,
            [FromHeader(Name = "Idempotency-Key")] string idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return BadRequest(new { error = "Idempotency-Key header is required." }); // 400

            if (request == null || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Currency))
                return UnprocessableEntity(new { error = "Invalid payment request." }); // 422

            var result = await _paymentService.ProcessPaymentAsync(idempotencyKey, request);

            return result.Status switch
            {
                PaymentResultStatus.Created => StatusCode(201, result.Payment),          // new payment created
                PaymentResultStatus.AlreadyProcessed => StatusCode(200, result.Payment),  // idempotent replay
                PaymentResultStatus.InProgress => StatusCode(409, new { error = "Payment is already being processed." }),
                PaymentResultStatus.Failed => StatusCode(502, new { error = result.ErrorMessage }), // upstream gateway failure
                _ => StatusCode(500, new { error = "Unexpected error." })
            };
        }
    }

    public class PaymentRequest
    {
        public Guid CustomerId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "BDT";
        public string PaymentMethod { get; set; } = default!;
    }

    public enum PaymentStatus { Processing, Completed, Failed }

    public class Payment
    {
        public Guid Id { get; set; }
        public string IdempotencyKey { get; set; } = default!; // UNIQUE index in DB
        public Guid CustomerId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = default!;
        public PaymentStatus Status { get; set; }
        public string? GatewayTransactionId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    public enum PaymentResultStatus { Created, AlreadyProcessed, InProgress, Failed }

    public class PaymentOperationResult
    {
        public PaymentResultStatus Status { get; set; }
        public Payment? Payment { get; set; }
        public string? ErrorMessage { get; set; }

        public static PaymentOperationResult Created(Payment p) => new() { Status = PaymentResultStatus.Created, Payment = p };
        public static PaymentOperationResult AlreadyProcessed(Payment p) => new() { Status = PaymentResultStatus.AlreadyProcessed, Payment = p };
        public static PaymentOperationResult InProgress() => new() { Status = PaymentResultStatus.InProgress };
        public static PaymentOperationResult FailedResult(string message) => new() { Status = PaymentResultStatus.Failed, ErrorMessage = message };
    }

    public interface IPaymentService
    {
        Task<PaymentOperationResult> ProcessPaymentAsync(string idempotencyKey, PaymentRequest request);
    }

    // Minimal abstraction over a distributed lock provider (e.g. RedLock.net over Redis).
    public interface IDistributedLockProvider
    {
        Task<IAsyncDisposable?> AcquireAsync(string resource, TimeSpan expiry, TimeSpan wait);
    }

    public interface IPaymentGatewayClient
    {
        Task<GatewayChargeResult> ChargeAsync(Guid paymentId, decimal amount, string currency, string paymentMethod);
    }

    public class GatewayChargeResult
    {
        public bool Success { get; set; }
        public string? TransactionId { get; set; }
    }

    public class PaymentService : IPaymentService
    {
        private readonly AppDbContext _context;
        private readonly IDistributedLockProvider _lockProvider;
        private readonly IPaymentGatewayClient _gatewayClient;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(
            AppDbContext context,
            IDistributedLockProvider lockProvider,
            IPaymentGatewayClient gatewayClient,
            ILogger<PaymentService> logger)
        {
            _context = context;
            _lockProvider = lockProvider;
            _gatewayClient = gatewayClient;
            _logger = logger;

            // Exponential backoff: 200ms, 400ms, 800ms. Only retry on transient
            // network/timeout errors -- never retry on a definite gateway decline.
            // Includes HttpRequestException (connection, DNS failures) and TimeoutException.
            _retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutException>()
                .Or<InvalidOperationException>()  // connection pooling exhaustion
                .WaitAndRetryAsync(3,
                    attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1)),
                    onRetry: (ex, delay, attempt, _) =>
                        _logger.LogWarning(ex, "Payment gateway retry {Attempt}/{MaxRetries} after {Delay}ms", 
                            attempt, 3, delay.TotalMilliseconds));
        }

        public async Task<PaymentOperationResult> ProcessPaymentAsync(string idempotencyKey, PaymentRequest request)
        {
            // 1. Fast path -- has this idempotency key already been recorded?
            var existing = await _context.Payments
                .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey);

            if (existing != null)
                return existing.Status == PaymentStatus.Completed
                    ? PaymentOperationResult.AlreadyProcessed(existing)
                    : PaymentOperationResult.InProgress();

            // 2. Distributed lock scoped to the idempotency key, so only one
            //    request -- even across multiple app instances -- processes
            //    this specific payment at a time.
            await using var @lock = await _lockProvider.AcquireAsync(
                resource: $"payment:{idempotencyKey}",
                expiry: TimeSpan.FromSeconds(30),
                wait: TimeSpan.FromSeconds(5));

            if (@lock == null)
                return PaymentOperationResult.InProgress(); // another request holds the lock

            // 3. Re-check inside the lock (double-checked locking) in case another
            //    request finished while we were waiting to acquire it.
            existing = await _context.Payments.FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey);
            if (existing != null)
                return existing.Status == PaymentStatus.Completed
                    ? PaymentOperationResult.AlreadyProcessed(existing)
                    : PaymentOperationResult.InProgress();

            // 4. Persist a "Processing" row first, inside its own short transaction.
            //    IdempotencyKey has a UNIQUE DB constraint as defense-in-depth: even
            //    if two requests somehow slipped past the lock, the DB rejects the
            //    second insert instead of allowing a duplicate charge.
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = idempotencyKey,
                CustomerId = request.CustomerId,
                Amount = request.Amount,
                Currency = request.Currency,
                Status = PaymentStatus.Processing,
                CreatedAtUtc = DateTime.UtcNow
            };

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return PaymentOperationResult.InProgress(); // unique constraint hit = concurrent duplicate
            }

            // 5. Call the external gateway with retry, outside the DB transaction --
            //    never hold a DB transaction open across a network call.
            try
            {
                var gatewayResult = await _retryPolicy.ExecuteAsync(() =>
                    _gatewayClient.ChargeAsync(payment.Id, request.Amount, request.Currency, request.PaymentMethod));

                payment.Status = gatewayResult.Success ? PaymentStatus.Completed : PaymentStatus.Failed;
                payment.GatewayTransactionId = gatewayResult.TransactionId;
                payment.CompletedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return gatewayResult.Success
                    ? PaymentOperationResult.Created(payment)
                    : PaymentOperationResult.FailedResult("Payment gateway declined the transaction.");
            }
            catch (Exception ex)
            {
                payment.Status = PaymentStatus.Failed;
                await _context.SaveChangesAsync();
                _logger.LogError(ex, "Payment {IdempotencyKey} failed after retries", idempotencyKey);
                return PaymentOperationResult.FailedResult("Payment gateway is unavailable. Please retry.");
            }
        }
    }
}

// DB schema note:
// CREATE UNIQUE INDEX UX_Payments_IdempotencyKey ON Payments (IdempotencyKey);
