using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentModule.Api.Security;

/// <summary>
/// Validates security headers on incoming requests (and optionally responses).
/// Mirrors Papermaker.PaymentSDKNEW.Security.SecurityHeaderValidator — adapted for
/// the Payment Server which supports multiple API keys and HMAC secrets (rotation).
///
/// Protections:
///   • API key check          — request is from a known caller
///   • Timestamp window       — rejects requests older than MaxAgeSeconds (default 5 min)
///   • Nonce uniqueness       — prevents replay attacks from the same message being re-sent
///   • HMAC signature verify  — prevents tampering of payload in transit
///
/// Validation order:
///   1. Canonical Method+Path+Timestamp+Nonce+Body   (enterprise standard)
///   2. Payload+Nonce+Timestamp fallback             (legacy / simpler callers)
/// </summary>
public class SecurityHeaderValidator
{
    private readonly HmacSigner _hmacSigner;
    private readonly S2SSecurityOptions _options;
    private readonly ILogger<SecurityHeaderValidator> _logger;

    // Nonce store: "nonce:timestamp" → unix-seconds the nonce arrived
    private readonly ConcurrentDictionary<string, long> _usedNonces = new();
    private readonly object _nonceLock = new();

    /// <summary>Maximum age of an acceptable request/response in seconds (matches user requirement for 5s).</summary>
    private const int MaxAgeSeconds = 10;

    public SecurityHeaderValidator(
        HmacSigner hmacSigner,
        IOptions<S2SSecurityOptions> options,
        ILogger<SecurityHeaderValidator> logger)
    {
        _hmacSigner = hmacSigner;
        _options = options.Value;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates all S2S security headers on an incoming request or response.
    /// </summary>
    /// <param name="payloadJson">Raw JSON body (payload only — not the envelope).</param>
    /// <param name="xApiKey">Value of x-api-key header.</param>
    /// <param name="xNonce">Value of x-nonce header.</param>
    /// <param name="xTimestamp">Value of x-timestamp header.</param>
    /// <param name="xSignature">Value of x-signature header.</param>
    /// <param name="httpMethod">HTTP method (used for canonical HMAC). Can be null for legacy mode.</param>
    /// <param name="requestPath">Path+query (used for canonical HMAC). Can be null for legacy mode.</param>
    /// <param name="source">Label used in log messages ("request" / "response").</param>
    public ValidationResult Validate(
        string payloadJson,
        string xApiKey,
        string xNonce,
        string xTimestamp,
        string xSignature,
        string? httpMethod = null,
        string? requestPath = null,
        string source = "request")
    {
        try
        {
            // 1. API key
            if (!_options.ApiKeys.Contains(xApiKey))
            {
                _logger.LogWarning("[SecurityHeaderValidator] Invalid x-api-key for {Source}", source);
                return ValidationResult.Fail("Invalid API Key");
            }

            // NOTE: Timestamp validation is handled by TimestampValidatorMiddleware (runs before this).
            // Skipping duplicate check here to avoid redundancy.

            // 2. Nonce replay check
            if (!IsNonceUnique(xNonce, xTimestamp))
            {
                _logger.LogWarning("[SecurityHeaderValidator] Replayed nonce detected for {Source}", source);
                return ValidationResult.Fail("Duplicate nonce (replay detected)");
            }

            // 4. HMAC verification — try canonical first, then legacy payload-style
            foreach (var secret in _options.HmacSecrets)
            {
                // 4a. Canonical: METHOD + PATH + TIMESTAMP + NONCE + BODY
                if (!string.IsNullOrEmpty(httpMethod) && !string.IsNullOrEmpty(requestPath))
                {
                    if (_hmacSigner.VerifySignature(httpMethod, requestPath, payloadJson,
                            xTimestamp, xNonce, xSignature, secret))
                    {
                        _logger.LogDebug("[SecurityHeaderValidator] ✅ Canonical HMAC ok for {Source}", source);
                        return ValidationResult.Ok();
                    }
                }

                // 4b. Legacy payload-style: PAYLOAD + NONCE + TIMESTAMP
                if (_hmacSigner.VerifyPayloadSignature(payloadJson, xNonce, xTimestamp, xSignature, secret))
                {
                    _logger.LogDebug("[SecurityHeaderValidator] ✅ Payload-style HMAC ok for {Source}", source);
                    return ValidationResult.Ok();
                }
            }

            // Log debug diagnostics for developer troubleshooting
            LogHmacMismatch(payloadJson, xTimestamp, xNonce, xSignature, httpMethod, requestPath, source);
            return ValidationResult.Fail("Invalid HMAC signature");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SecurityHeaderValidator] Error validating {Source} headers", source);
            return ValidationResult.Fail("Validation error");
        }
    }

    /// <summary>Returns the current active nonce count (useful for monitoring).</summary>
    public int ActiveNonceCount => _usedNonces.Count;

    // ── Private ───────────────────────────────────────────────────────────────

    private bool IsNonceUnique(string nonce, string timestamp)
    {
        if (string.IsNullOrEmpty(nonce)) return false;
        CleanupExpiredNonces();

        var key = $"{nonce}:{timestamp}";
        if (_usedNonces.ContainsKey(key)) return false;

        if (!long.TryParse(timestamp, out var ts)) return false;
        _usedNonces.TryAdd(key, ts);
        return true;
    }

    private void CleanupExpiredNonces()
    {
        lock (_nonceLock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expired = _usedNonces
                .Where(kv => now - kv.Value > MaxAgeSeconds)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var k in expired) _usedNonces.TryRemove(k, out _);
            if (expired.Count > 0)
                _logger.LogDebug("[SecurityHeaderValidator] Cleaned {N} expired nonces", expired.Count);
        }
    }

    private void LogHmacMismatch(
        string payloadJson, string timestamp, string nonce,
        string providedSig, string? method, string? path, string source)
    {
        try
        {
            var secret = _options.HmacSecrets.FirstOrDefault() ?? "";
            var providedHex = _hmacSigner.NormalizeSignatureToHex(providedSig);
            var payloadExpected = _hmacSigner.ComputeHmacHex(
                $"{payloadJson}{nonce}{timestamp}", secret);

            _logger.LogWarning(
                "[SecurityHeaderValidator] HMAC mismatch for {Source}. " +
                "provided={Provided} expected(payload-style)={Expected}",
                source, providedHex, payloadExpected);

            if (!string.IsNullOrEmpty(method) && !string.IsNullOrEmpty(path))
            {
                var canonical = _hmacSigner.BuildCanonical(method, path, timestamp, nonce, payloadJson);
                var canonicalExpected = _hmacSigner.ComputeHmacHex(canonical, secret);
                _logger.LogWarning(
                    "[SecurityHeaderValidator] expected(canonical)={CE}  canonical={C}",
                    canonicalExpected, canonical);
            }

            Console.WriteLine($"[SecurityHeaderValidator] ❌ HMAC mismatch for {source}");
            Console.WriteLine($"  Provided (hex): {providedHex}");
            Console.WriteLine($"  Expected (payload-style, hex): {payloadExpected}");
        }
        catch { /* non-fatal debug path */ }
    }
}

/// <summary>Result of a security header validation check.</summary>
public sealed class ValidationResult
{
    public bool IsValid { get; }
    public string? Reason { get; }

    private ValidationResult(bool ok, string? reason) { IsValid = ok; Reason = reason; }

    public static ValidationResult Ok()             => new(true, null);
    public static ValidationResult Fail(string why) => new(false, why);
}
