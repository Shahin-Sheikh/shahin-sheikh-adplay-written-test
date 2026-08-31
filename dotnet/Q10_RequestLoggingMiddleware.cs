using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AdPlay.Api.Middleware
{
    // Q10. Middleware that logs request body, response body, execution time,
    // correlation ID, and unhandled exceptions -- without breaking the pipeline.
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Skip logging for health checks and metrics to reduce log noise
            if (context.Request.Path.StartsWithSegments("/health") ||
                context.Request.Path.StartsWithSegments("/metrics") ||
                context.Request.Path.StartsWithSegments("/ready"))
            {
                await _next(context);
                return;
            }

            var correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var cid)
                ? cid.ToString()
                : Guid.NewGuid().ToString();

            context.Items["CorrelationId"] = correlationId;
            context.Response.Headers["X-Correlation-Id"] = correlationId;

            using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                var requestBody = await ReadRequestBodyAsync(context.Request);

                // Swap in a buffer so we can read the response body after the
                // pipeline runs, then copy it back to the real stream.
                var originalResponseBodyStream = context.Response.Body;
                await using var responseBuffer = new MemoryStream();
                context.Response.Body = responseBuffer;

                var stopwatch = Stopwatch.StartNew();

                try
                {
                    await _next(context);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _logger.LogError(ex,
                        "Unhandled exception. CorrelationId={CorrelationId} Path={Path} Method={Method} ElapsedMs={ElapsedMs}",
                        correlationId, context.Request.Path, context.Request.Method, stopwatch.ElapsedMilliseconds);

                    // Re-throw so a global exception-handling middleware / status
                    // code page further up the pipeline still runs. We only log
                    // here, we don't swallow the error.
                    throw;
                }
                finally
                {
                    stopwatch.Stop();

                    responseBuffer.Seek(0, SeekOrigin.Begin);
                    var responseBody = await new StreamReader(responseBuffer).ReadToEndAsync();
                    responseBuffer.Seek(0, SeekOrigin.Begin);

                    await responseBuffer.CopyToAsync(originalResponseBodyStream);
                    context.Response.Body = originalResponseBodyStream;

                    // Use appropriate log level based on status code
                    var logLevel = context.Response.StatusCode >= 500 ? LogLevel.Error :
                                   context.Response.StatusCode >= 400 ? LogLevel.Warning :
                                   LogLevel.Information;

                    _logger.Log(logLevel,
                        "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms. RequestBody={RequestBody} ResponseBody={ResponseBody}",
                        context.Request.Method, context.Request.Path, context.Response.StatusCode,
                        stopwatch.ElapsedMilliseconds, MaskSensitiveData(requestBody), MaskSensitiveData(responseBody));
                }
            }
        }

        private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
        {
            request.EnableBuffering(); // allows the body to be read again downstream
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0; // reset so model binding can still read it
            return body;
        }

        /// <summary>
        /// Masks sensitive data (passwords, tokens, credit cards, SSN) in request/response bodies
        /// to prevent leaking secrets into logs.
        /// </summary>
        private static string MaskSensitiveData(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            // Mask password fields: "password": "actual_value" -> "password": "***"
            value = Regex.Replace(value,
                @"([""']?password[""']?\s*[:=]\s*[""'])([^""']+)([""'])",
                "$1***$3",
                RegexOptions.IgnoreCase);

            // Mask credit card numbers: "cardNumber": "1234..." -> "cardNumber": "****"
            value = Regex.Replace(value,
                @"([""']?card(?:Number|No|number)[""']?\s*[:=]\s*[""'])([^""']+)([""'])",
                "$1****$3",
                RegexOptions.IgnoreCase);

            // Mask authentication tokens: "token": "eyJ..." -> "token": "***"
            value = Regex.Replace(value,
                @"([""']?(?:token|accessToken|refreshToken|authorization)[""']?\s*[:=]\s*[""'])([^""']+)([""'])",
                "$1***$3",
                RegexOptions.IgnoreCase);

            // Mask SSN: "ssn": "123-45-6789" -> "ssn": "***-**-****"
            value = Regex.Replace(value,
                @"([""']?ssn[""']?\s*[:=]\s*[""'])(\d{3})-(\d{2})-(\d{4})([""'])",
                "$1***-**-****$5",
                RegexOptions.IgnoreCase);

            return value;
        }

        private static string Truncate(string value, int max = 2000) =>
            string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max] + "...(truncated)");
    }

    public static class RequestLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
            => app.UseMiddleware<RequestLoggingMiddleware>();
    }
}

// Registration in Program.cs, placed early in the pipeline so it wraps everything:
//   app.UseRequestLogging();
