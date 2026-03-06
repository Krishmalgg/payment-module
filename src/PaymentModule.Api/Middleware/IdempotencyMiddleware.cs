using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;

namespace PaymentModule.Api.Middleware;

/// <summary>
/// Runs AFTER TimestampValidatorMiddleware, BEFORE NewS2SSecurityMiddleware.
/// Checks idempotency key — returns cached response if duplicate, stores response if new.
/// DB logic lives in IRequestValidatorService (shared with RabbitMQ path).
/// </summary>
public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;
    private const string IdempotencyKeyHeader = "x-idempotency-key";

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IRequestValidatorService validator)
    {
        if (!ShouldProcessRequest(context.Request))
        {
            await _next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers[IdempotencyKeyHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(idempotencyKey))
        {
            _logger.LogInformation("[Idempotency] No x-idempotency-key header on {Method} {Path} — skipping.",
                context.Request.Method, context.Request.Path);
            await _next(context);
            return;
        }

        _logger.LogInformation("[Idempotency] Checking key={Key} Method={Method} Path={Path}",
            idempotencyKey, context.Request.Method, context.Request.Path);

        // Hash the body to detect key reuse with different payload
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;
        var requestHash = ComputeHash(requestBody);

        // Check against store
        var result = await validator.CheckIdempotencyAsync(idempotencyKey, requestHash);
        if (result.IsDuplicate)
        {
            _logger.LogInformation("[Idempotency] Duplicate detected — returning cached response. Key={Key}", idempotencyKey);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var body = result.CachedResponse
                ?? "{\"error\":\"Duplicate request — original response unavailable\"}";
            await context.Response.WriteAsync(body);
            return;
        }

        // Capture the response so we can store it
        var originalBody = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        // Store only successful responses (2xx)
        if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            var response = await new StreamReader(responseBody).ReadToEndAsync();
            var requestType = GetRequestType(context.Request.Path);

            try
            {
                await validator.StoreIdempotencyAsync(idempotencyKey, requestHash, response, "Http", requestType);
                _logger.LogInformation("[Idempotency] Stored record. Key={Key} Type={Type} Status={Status}",
                    idempotencyKey, requestType, context.Response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Idempotency] Failed to store record — possibly migration not applied. Key={Key} Type={Type}",
                    idempotencyKey, requestType);
            }

            responseBody.Seek(0, SeekOrigin.Begin);
        }
        else
        {
            _logger.LogDebug("[Idempotency] Not storing — response status {Status} is not 2xx. Key={Key}",
                context.Response.StatusCode, idempotencyKey);
        }

        await responseBody.CopyToAsync(originalBody);
    }

    private static bool ShouldProcessRequest(HttpRequest request)
    {
        var method = request.Method.ToUpperInvariant();
        return method == "POST" || method == "PUT" || method == "PATCH";
    }

    private static string GetRequestType(PathString path)
    {
        var p = path.Value?.ToLowerInvariant() ?? "";
        if (p.Contains("initiate"))  return "PaymentIntent";
        if (p.Contains("refund"))    return "Refund";
        if (p.Contains("add-card"))  return "AddCard";
        return "Unknown";
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(sha256.ComputeHash(bytes));
    }
}
