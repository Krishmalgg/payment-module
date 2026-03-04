using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentModule.Api.Security;

/// <summary>
/// Reusable service that computes HMAC-SHA256 over a payload and adds the resulting
/// security headers to any IHeaderDictionary (response or outgoing request).
///
/// TWO modes:
///   AddResponseHeaders  — for HTTP responses back to the Main Server.
///                         Includes x-signed-method and x-signed-path so the Main Server
///                         can reconstruct the exact canonical string.
///   AddRequestHeaders   — for outgoing requests FROM this server TO another service.
///                         Does NOT include x-signed-method / x-signed-path in headers
///                         (the receiver already knows the path it's listening on).
///
/// WHAT IS SIGNED:
///   The HMAC is always computed over the PAYLOAD ONLY — not the envelope wrapper.
///   Canonical = METHOD + PATH + TIMESTAMP + NONCE + PAYLOAD_JSON
///   This matches the Main Server SDK's GenerateSignature / VerifySignature contract.
/// </summary>
public class SecurityHeadersHandler
{
    private readonly HmacSigner _signer;
    private readonly S2SSecurityOptions _options;
    private readonly ILogger<SecurityHeadersHandler> _logger;

    public SecurityHeadersHandler(
        HmacSigner signer,
        IOptions<S2SSecurityOptions> options,
        ILogger<SecurityHeadersHandler> logger)
    {
        _signer = signer;
        _options = options.Value;
        _logger = logger;
    }

    // ── Response Headers (includes x-signed-method + x-signed-path) ──────────

    /// <summary>
    /// Signs the payload and adds all security headers to a response.
    /// Returns the nonce, timestamp, and signature for logging/debugging.
    /// </summary>
    public (string Nonce, string Timestamp, string Signature) AddResponseHeaders(
        IHeaderDictionary headers,
        string method,
        string pathAndQuery,
        string payloadJson)
    {
        var (nonce, timestamp, signature) = Sign(method, pathAndQuery, payloadJson);

        var apiKey = _options.ApiKeys.FirstOrDefault() ?? "";
        headers["x-api-key"]       = apiKey;
        headers["x-nonce"]         = nonce;
        headers["x-timestamp"]     = timestamp;
        headers["x-signature"]     = signature;
        headers["x-signed-method"] = method;         // exact METHOD used in canonical
        headers["x-signed-path"]   = pathAndQuery;   // exact PATH used in canonical

        Console.WriteLine($"[SecurityHeadersHandler] Response x-api-key set to: {apiKey}");

        _logger.LogInformation(
            "[SecurityHeadersHandler] Response headers signed — {M} {P} sig={Sig}",
            method, pathAndQuery, signature[..Math.Min(12, signature.Length)] + "…");

        return (nonce, timestamp, signature);
    }

    // ── Request Headers (outgoing request, no method/path headers) ───────────

    /// <summary>
    /// Signs the payload and adds security headers for an outgoing request.
    /// The receiver reconstructs the path from its own request context, so
    /// x-signed-method and x-signed-path are NOT added here.
    /// </summary>
    public (string Nonce, string Timestamp, string Signature) AddRequestHeaders(
        IHeaderDictionary headers,
        string method,
        string pathAndQuery,
        string payloadJson)
    {
        var (nonce, timestamp, signature) = Sign(method, pathAndQuery, payloadJson);

        var apiKey = _options.ApiKeys.FirstOrDefault() ?? "";
        headers["x-api-key"]   = apiKey;
        headers["x-nonce"]     = nonce;
        headers["x-timestamp"] = timestamp;
        headers["x-signature"] = signature;

        Console.WriteLine($"[SecurityHeadersHandler] Request x-api-key set to: {apiKey}");

        _logger.LogInformation(
            "[SecurityHeadersHandler] Request headers signed — {M} {P}",
            method, pathAndQuery);

        return (nonce, timestamp, signature);
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private (string Nonce, string Timestamp, string Signature) Sign(
        string method, string pathAndQuery, string payloadJson)
    {
        var nonce     = _signer.GenerateNonce();
        var timestamp = _signer.GetTimestamp();
        var hmacSecret = _options.HmacSecrets.FirstOrDefault() ?? "";

        // Sign the PAYLOAD only — not the envelope wrapper
        var signature = _signer.GenerateSignature(method, pathAndQuery, payloadJson, nonce, timestamp, hmacSecret);

        Console.WriteLine($"[SecurityHeadersHandler] Method   : {method}");
        Console.WriteLine($"[SecurityHeadersHandler] Path     : {pathAndQuery}");
        Console.WriteLine($"[SecurityHeadersHandler] Timestamp: {timestamp}");
        Console.WriteLine($"[SecurityHeadersHandler] Nonce    : {nonce}");
        Console.WriteLine($"[SecurityHeadersHandler] Payload  : {payloadJson}");
        Console.WriteLine($"[SecurityHeadersHandler] Canonical: {_signer.BuildCanonical(method, pathAndQuery, timestamp, nonce, payloadJson)}");
        Console.WriteLine($"[SecurityHeadersHandler] Signature: {signature}");
        Console.WriteLine($"[SecurityHeadersHandler] Secret   : {_signer.MaskSecret(hmacSecret)}");

        return (nonce, timestamp, signature);
    }
}
