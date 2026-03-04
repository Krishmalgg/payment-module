using System.Text;

namespace PaymentModule.Api.Security;

/// <summary>
/// NEW S2S request-validation middleware.
/// Activated when Security:IsNewS2sValidation = true.
/// Delegates all validation rules (API key, timestamp, nonce, HMAC) to SecurityHeaderValidator,
/// keeping this class focused on HTTP concerns only: path guard, body buffering, response on failure.
/// The existing S2SSecurityMiddleware is NOT registered in that case.
/// </summary>
public class NewS2SSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeaderValidator _validator;
    private readonly ILogger<NewS2SSecurityMiddleware> _logger;

    public NewS2SSecurityMiddleware(
        RequestDelegate next,
        SecurityHeaderValidator validator,
        ILogger<NewS2SSecurityMiddleware> logger)
    {
        _next      = next;
        _validator = validator;
        _logger    = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Webhooks are signed by external providers — skip S2S check
        if (context.Request.Path.StartsWithSegments("/api/v1/webhooks"))
        {
            Console.WriteLine($"[NewS2S] ⏭ Skipping webhook: {context.Request.Path}");
            await _next(context);
            return;
        }

        if (!IsSecuredPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        Console.WriteLine($"[NewS2S] ▶ {context.Request.Method} {context.Request.Path}");
        Console.WriteLine("[NewS2S] --- Headers ---");
        foreach (var h in context.Request.Headers)
            Console.WriteLine($"[NewS2S]   {h.Key}: {h.Value}");

        // Read body (EnableBuffering so the controller can re-read it)
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var xApiKey   = context.Request.Headers["x-api-key"].ToString();
        var xNonce    = context.Request.Headers["x-nonce"].ToString();
        var xTs       = context.Request.Headers["x-timestamp"].ToString();
        var xSig      = context.Request.Headers["x-signature"].ToString();
        var method    = context.Request.Method.ToUpperInvariant();
        var path      = (context.Request.PathBase + context.Request.Path + context.Request.QueryString).ToString();

        Console.WriteLine("[NewS2S] --- HMAC inputs ---");
        Console.WriteLine($"[NewS2S]   Method   : {method}");
        Console.WriteLine($"[NewS2S]   Path     : {path}");
        Console.WriteLine($"[NewS2S]   Timestamp: {xTs}");
        Console.WriteLine($"[NewS2S]   Nonce    : {xNonce}");
        Console.WriteLine($"[NewS2S]   Body     : {body}");
        Console.WriteLine($"[NewS2S]   Signature: {xSig}");

        var result = _validator.Validate(
            payloadJson: body,
            xApiKey:     xApiKey,
            xNonce:      xNonce,
            xTimestamp:  xTs,
            xSignature:  xSig,
            httpMethod:  method,
            requestPath: path,
            source:      "incoming-request");

        if (!result.IsValid)
        {
            Console.WriteLine($"[NewS2S] ❌ Validation failed: {result.Reason}");
            _logger.LogWarning("[NewS2S] Rejected {Method} {Path} — {Reason}", method, path, result.Reason);
            context.Response.StatusCode  = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"error\":\"Unauthorized\",\"reason\":\"{result.Reason}\"}}");
            return;
        }

        Console.WriteLine("[NewS2S] ✅ Validation passed");
        context.Request.Body.Position = 0;
        await _next(context);
    }

    private static bool IsSecuredPath(PathString path) =>
        path.StartsWithSegments("/api/v1/payments/initiate") ||
        path.StartsWithSegments("/api/v1/payments/intents") ||
        path.StartsWithSegments("/api/v1/payments/add-card") ||
        path.StartsWithSegments("/api/v1/communication/add-card") ||
        path.StartsWithSegments("/api/v1/payments/refund") ||
        path.StartsWithSegments("/api/v1/payments/stored-cards");
}
