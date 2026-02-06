using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PaymentModule.Api.Middleware;

public class S2SSecurityOptions
{
    public string[] ApiKeys { get; set; } = [];
    public string[] HmacSecrets { get; set; } = [];
}

public class S2SSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly S2SSecurityOptions _options;
    private readonly ILogger<S2SSecurityMiddleware> _logger;

    public S2SSecurityMiddleware(RequestDelegate next, IOptions<S2SSecurityOptions> options, ILogger<S2SSecurityMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to payment intent endpoint (or configured paths)
        // Exempt webhooks from S2S security as they are called by external providers
        if (context.Request.Path.StartsWithSegments("/api/v1/webhooks"))
        {
            Console.WriteLine($"[S2S Security] Skipping security check for public webhook: {context.Request.Path}");
            await _next(context);
            return;
        }

        if (!context.Request.Path.StartsWithSegments("/api/v1/payments/intents") &&
            !context.Request.Path.StartsWithSegments("/api/v1/payments/add-card") &&
            !context.Request.Path.StartsWithSegments("/api/v1/communication/add-card") &&
            !context.Request.Path.StartsWithSegments("/api/v1/payments/stored-cards"))
        {
            await _next(context);
            return;
        }

        if (!CheckHeaders(context)) return;
        
        // Log arrivals for debugging
        Console.WriteLine($"[S2S Security] Request to {context.Request.Path}");
        Console.WriteLine($"  -> x-api-key: {context.Request.Headers["x-api-key"]}");
        Console.WriteLine($"  -> x-signature: {context.Request.Headers["x-signature"]}");
        Console.WriteLine($"  -> x-timestamp: {context.Request.Headers["x-timestamp"]}");
        Console.WriteLine($"  -> x-nonce: {context.Request.Headers["x-nonce"]}");

        if (!CheckApiKey(context)) return;
        if (!CheckTimestamp(context)) return;
        
        // Body reading required for HMAC
        context.Request.EnableBuffering();
        
        if (!await CheckHmacSignature(context)) return;

        context.Request.Body.Position = 0; // Reset for next middleware
        await _next(context);
    }

    private bool CheckHeaders(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey("x-api-key") ||
            !context.Request.Headers.ContainsKey("x-signature") ||
            !context.Request.Headers.ContainsKey("x-timestamp") ||
            !context.Request.Headers.ContainsKey("x-nonce"))
        {
            RespondUnauthorized(context, "Missing Security Headers");
            return false;
        }
        return true;
    }

    private bool CheckApiKey(HttpContext context)
    {
        var apiKey = context.Request.Headers["x-api-key"].ToString();
        if (!_options.ApiKeys.Contains(apiKey))
        {
            RespondUnauthorized(context, "Invalid API Key");
            return false;
        }
        return true;
    }

    private bool CheckTimestamp(HttpContext context)
    {
        return true; 
    }

    private async Task<bool> CheckHmacSignature(HttpContext context)
    {
        var receivedSignature = context.Request.Headers["x-signature"].ToString();
        var timestamp = context.Request.Headers["x-timestamp"].ToString();
        var nonce = context.Request.Headers["x-nonce"].ToString();
        var method = context.Request.Method.ToUpperInvariant();
        // Construct canonical path: original URL including query string if any
        var pd = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        // Canonical String: Method (UPPER) + Path + Timestamp + Nonce + Body
        var payload = $"{method}{pd}{timestamp}{nonce}{body}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        foreach (var secret in _options.HmacSecrets)
        {
            var hmacBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payloadBytes);
            var computedSignature = Convert.ToHexString(hmacBytes).ToLowerInvariant();

                Console.WriteLine($"[S2S Security] ✅ Computed Signature: {computedSignature}");
                if (CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature), 
                Encoding.UTF8.GetBytes(receivedSignature)))
            {
                return true;
            }
        }

        // Debug help for developer
        if (_options.HmacSecrets.Any())
        {
            var debugHmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.HmacSecrets[0]), payloadBytes);
            var debugSig = Convert.ToHexString(debugHmac).ToLowerInvariant();
            Console.WriteLine($"[S2S Security] ❌ Signature Mismatch!");
            Console.WriteLine($"  -> Expected signature for first secret: {debugSig}");
            Console.WriteLine($"  -> Canonical String used: {payload}");
        }

        RespondUnauthorized(context, "Invalid Signature");
        return false;
    }

    private void RespondUnauthorized(HttpContext context, string message)
    {
        _logger.LogWarning($"S2S Security Failed: {message}");
        context.Response.StatusCode = 401;
        context.Response.WriteAsync("Unauthorized");
    }
}
