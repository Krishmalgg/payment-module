using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;

namespace PaymentModule.Api.Middleware;

/// <summary>
/// Runs BEFORE S2S/HMAC middleware.
/// Rejects stale requests instantly (nanoseconds) without touching crypto.
/// Only applies to state-changing methods: POST, PUT, PATCH.
/// </summary>
public class TimestampValidatorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TimestampValidatorMiddleware> _logger;
    private const string TimestampHeader = "x-timestamp";
    private const int WindowSeconds = 30;

    public TimestampValidatorMiddleware(RequestDelegate next, ILogger<TimestampValidatorMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IRequestValidatorService validator)
    {
        var method = context.Request.Method.ToUpperInvariant();
        if (method != "POST" && method != "PUT" && method != "PATCH")
        {
            await _next(context);
            return;
        }

        var timestampHeader = context.Request.Headers[TimestampHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(timestampHeader) || !long.TryParse(timestampHeader, out var unixTimestamp))
        {
            _logger.LogWarning("[TimestampValidator] Missing or invalid x-timestamp header. Method={Method} Path={Path}",
                method, context.Request.Path);
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Missing or invalid x-timestamp header\"}");
            return;
        }

        if (!validator.ValidateTimestamp(unixTimestamp, WindowSeconds))
        {
            _logger.LogWarning("[TimestampValidator] Rejected stale request. Method={Method} Path={Path} Timestamp={Ts}",
                method, context.Request.Path, unixTimestamp);
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Request timestamp expired\"}");
            return;
        }

        _logger.LogInformation("[TimestampValidator] Timestamp OK. Method={Method} Path={Path}", method, context.Request.Path);
        await _next(context);
    }
}
