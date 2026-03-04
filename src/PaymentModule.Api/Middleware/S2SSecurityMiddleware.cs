// using System.Security.Cryptography;
// using System.Text;
// using Microsoft.Extensions.Options;

// namespace PaymentModule.Api.Middleware;

// public class S2SSecurityOptions
// {
//     public string[] ApiKeys { get; set; } = [];
//     public string[] HmacSecrets { get; set; } = [];
// }

// public class S2SSecurityMiddleware
// {
//     private readonly RequestDelegate _next;
//     private readonly S2SSecurityOptions _options;
//     private readonly ILogger<S2SSecurityMiddleware> _logger;

//     public S2SSecurityMiddleware(RequestDelegate next, IOptions<S2SSecurityOptions> options, ILogger<S2SSecurityMiddleware> logger)
//     {
//         _next = next;
//         _options = options.Value;
//         _logger = logger;
//     }

//     public async Task InvokeAsync(HttpContext context)
//     {
//         // Only apply to payment intent endpoint (or configured paths)
//         // Exempt webhooks from S2S security as they are called by external providers
//         if (context.Request.Path.StartsWithSegments("/api/v1/webhooks"))
//         {
//             Console.WriteLine($"[S2S Security] Skipping security check for public webhook: {context.Request.Path}");
//             await _next(context);
//             return;
//         }

//         if (!context.Request.Path.StartsWithSegments("/api/v1/payments/intents") &&
//             !context.Request.Path.StartsWithSegments("/api/v1/payments/add-card") &&
//             !context.Request.Path.StartsWithSegments("/api/v1/communication/add-card") &&
//             !context.Request.Path.StartsWithSegments("/api/v1/payments/refund") &&
//             !context.Request.Path.StartsWithSegments("/api/v1/payments/stored-cards"))
//         {
//             await _next(context);
//             return;
//         }

//         // Log incoming headers immediately (before any header validation)
//         _logger.LogInformation("[S2S Security] Incoming request to {Path}", context.Request.Path);
//         foreach (var header in context.Request.Headers)
//         {
//             _logger.LogInformation("[S2S Security] Header {Header}: {Value}", header.Key, header.Value.ToString());
//         }

//         if (!CheckHeaders(context)) return;

//         if (!CheckApiKey(context)) return;
//         if (!CheckTimestamp(context)) return;
        
//         // Body reading required for HMAC
//         context.Request.EnableBuffering();
        
//         if (!await CheckHmacSignature(context)) return;

//         // // Log the refund payload if checking refund endpoint
//         // if (context.Request.Path.StartsWithSegments("/api/v1/payments/refund"))
//         // {
//         //     context.Request.Body.Position = 0;
//         //     using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
//         //     var body = await reader.ReadToEndAsync();
//         //     _logger.LogInformation("Incoming Refund Request Payload: {Payload}", body);
//         //     context.Request.Body.Position = 0;
//         // }

//         context.Request.Body.Position = 0; // Reset for next middleware
//         await _next(context);
//     }

//     private bool CheckHeaders(HttpContext context)
//     {
//         if (!context.Request.Headers.ContainsKey("x-api-key") ||
//             !context.Request.Headers.ContainsKey("x-signature") ||
//             !context.Request.Headers.ContainsKey("x-timestamp") ||
//             !context.Request.Headers.ContainsKey("x-nonce"))
//         {
//             RespondUnauthorized(context, "Missing Security Headers");
//             return false;
//         }
//         return true;
//     }

//     private bool CheckApiKey(HttpContext context)
//     {
//         var apiKey = context.Request.Headers["x-api-key"].ToString();
//         if (!_options.ApiKeys.Contains(apiKey))
//         {
//             RespondUnauthorized(context, "Invalid API Key");
//             return false;
//         }
//         return true;
//     }

//     private bool CheckTimestamp(HttpContext context)
//     {
//         return true; 
//     }
// private async Task<bool> CheckHmacSignature(HttpContext context)
// {
//     var receivedSignature = context.Request.Headers["x-signature"].ToString();
//     var timestamp = context.Request.Headers["x-timestamp"].ToString();
//     var nonce = context.Request.Headers["x-nonce"].ToString();
//     var method = context.Request.Method.ToUpperInvariant();

//     // Prefer signed path header if sender provided it (path+query)
//     var signedPathHeader = context.Request.Headers.ContainsKey("x-signed-path")
//         ? context.Request.Headers["x-signed-path"].ToString()
//         : null;

//     var pd = !string.IsNullOrEmpty(signedPathHeader)
//         ? signedPathHeader
//         : (context.Request.PathBase + context.Request.Path + context.Request.QueryString);

//     using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
//     var body = await reader.ReadToEndAsync();

//     var payload = $"{method}{pd}{timestamp}{nonce}{body}";
//     var payloadBytes = Encoding.UTF8.GetBytes(payload);

//     // Try to decode the received signature (support base64 and hex)
//     if (!TryDecodeSignature(receivedSignature, out var providedBytes, out var inputFormat))
//     {
//         Console.WriteLine("[S2S Security] ❌ Unable to parse provided signature (not base64 or hex).");
//         RespondUnauthorized(context, "Invalid Signature Format");
//         return false;
//     }

//     foreach (var secret in _options.HmacSecrets)
//     {
//         var expectedBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payloadBytes);

//         var expectedHex = Convert.ToHexString(expectedBytes).ToLowerInvariant();
//         var providedHex = Convert.ToHexString(providedBytes).ToLowerInvariant();

//         Console.WriteLine($"[S2S Security] Canonical: {payload}");
//         Console.WriteLine($"[S2S Security] Provided signature format: {inputFormat}, parsed hex: {providedHex}");
//         Console.WriteLine($"[S2S Security] Computed expected hex: {expectedHex}");

//         if (providedBytes.Length == expectedBytes.Length &&
//             CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
//         {
//             Console.WriteLine("[S2S Security] ✅ Signature verified (raw bytes match).");
//             return true;
//         }
//     }

//     // Developer debug help
//     if (_options.HmacSecrets.Any())
//     {
//         var debugHmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.HmacSecrets[0]), payloadBytes);
//         var debugSig = Convert.ToHexString(debugHmac).ToLowerInvariant();
//         Console.WriteLine($"[S2S Security] ❌ Signature Mismatch!");
//         Console.WriteLine($"  -> Expected signature for first secret (hex): {debugSig}");
//         Console.WriteLine($"  -> Canonical String used: {payload}");
//     }

//     RespondUnauthorized(context, "Invalid Signature");
//     return false;
// }

// private static bool TryDecodeSignature(string sig, out byte[] bytes, out string format)
// {
//     bytes = Array.Empty<byte>();
//     format = "unknown";

//     if (string.IsNullOrWhiteSpace(sig)) return false;

//     // Try base64 first
//     try
//     {
//         bytes = Convert.FromBase64String(sig);
//         format = "base64";
//         return true;
//     }
//     catch { /* not base64 */ }

//     // Try hex (allow mixed case, optional 0x prefix, no separators)
//     var hex = sig.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? sig.Substring(2) : sig;
//     hex = hex.Replace(" ", "");
//     if (hex.Length % 2 != 0) return false;

//     try
//     {
//         var outBytes = new byte[hex.Length / 2];
//         for (int i = 0; i < outBytes.Length; i++)
//         {
//             outBytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
//         }
//         bytes = outBytes;
//         format = "hex";
//         return true;
//     }
//     catch
//     {
//         return false;
//     }
// }
    // private async Task<bool> CheckHmacSignaturee(HttpContext context)
    // {
    //     var receivedSignature = context.Request.Headers["x-signature"].ToString();
    //     var timestamp = context.Request.Headers["x-timestamp"].ToString();
    //     var nonce = context.Request.Headers["x-nonce"].ToString();
    //     var method = context.Request.Method.ToUpperInvariant();
    //     // Construct canonical path: original URL including query string if any
    //     var pd = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        
    //     using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
    //     var body = await reader.ReadToEndAsync();

    //     // Canonical String: Method (UPPER) + Path + Timestamp + Nonce + Body
    //     var payload = $"{method}{pd}{timestamp}{nonce}{body}";
    //     var payloadBytes = Encoding.UTF8.GetBytes(payload);

    //     foreach (var secret in _options.HmacSecrets)
    //     {
    //         var hmacBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payloadBytes);
    //         var computedSignature = Convert.ToHexString(hmacBytes).ToLowerInvariant();

    //             Console.WriteLine($"[S2S Security] ✅ Computed Signature: {computedSignature}");
    //             if (CryptographicOperations.FixedTimeEquals(
    //             Encoding.UTF8.GetBytes(computedSignature), 
    //             Encoding.UTF8.GetBytes(receivedSignature)))
    //         {
    //             return true;
    //         }
    //     }

    //     // Debug help for developer
    //     if (_options.HmacSecrets.Any())
    //     {
    //         var debugHmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.HmacSecrets[0]), payloadBytes);
    //         var debugSig = Convert.ToHexString(debugHmac).ToLowerInvariant();
    //         Console.WriteLine($"[S2S Security] ❌ Signature Mismatch!");
    //         Console.WriteLine($"  -> Expected signature for first secret: {debugSig}");
    //         Console.WriteLine($"  -> Canonical String used: {payload}");
    //     }

    //     RespondUnauthorized(context, "Invalid Signature");
    //     return false;
    // }

    // private void RespondUnauthorized(HttpContext context, string message)
    // {
    //     _logger.LogWarning($"S2S Security Failed: {message}");
    //     context.Response.StatusCode = 401;
    //     context.Response.WriteAsync("Unauthorized");
    // }
// }
