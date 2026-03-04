using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PaymentModule.Api.Security;

/// <summary>
/// Generates and verifies HMAC-SHA256 signatures for request/response authentication.
/// Mirrors Papermaker.PaymentSDKNEW.Security.HmacSigner so both sides use identical logic.
///
/// Canonical string format: METHOD (UPPER) + PATH+QUERY + TIMESTAMP + NONCE + BODY
/// Signature encoding: Base64 (output of GenerateSignature).
/// Verification: accepts Base64, URL-safe Base64, or hex input.
/// </summary>
public class HmacSigner
{
    private readonly ILogger<HmacSigner> _logger;

    public HmacSigner(ILogger<HmacSigner> logger)
    {
        _logger = logger;
    }

    // ── Canonical ───────────────────────────────────────────────────────────

    /// <summary>Builds the canonical string both signer and verifier must agree on.</summary>
    public string BuildCanonical(string method, string pathAndQuery, string timestamp, string nonce, string body)
        => $"{method.ToUpperInvariant()}{pathAndQuery}{timestamp}{nonce}{body}";

    // ── Signing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a Base64-encoded HMAC-SHA256 signature.
    /// Canonical = METHOD (UPPER) + PATH+QUERY + TIMESTAMP + NONCE + PAYLOAD
    /// </summary>
    public string GenerateSignature(
        string method, string pathAndQuery, string payload,
        string nonce, string timestamp, string hmacSecret)
    {
        var canonical = BuildCanonical(method, pathAndQuery, timestamp, nonce, payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hmacSecret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(hashBytes);
    }

    // ── Verification ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies an HMAC-SHA256 signature over the canonical Method+Path+Timestamp+Nonce+Body string.
    /// Accepts the provided signature as Base64, URL-safe Base64, or hex.
    /// </summary>
    public bool VerifySignature(
        string method, string pathAndQuery, string body,
        string timestamp, string nonce,
        string providedSignature, string hmacSecret)
    {
        if (string.IsNullOrWhiteSpace(providedSignature) || string.IsNullOrWhiteSpace(hmacSecret))
            return false;

        var canonical = BuildCanonical(method, pathAndQuery, timestamp, nonce, body);
        _logger.LogInformation("[HmacSigner] Verifying — secret={Mask} method={M} path={P}",
            MaskSecret(hmacSecret), method, pathAndQuery);
        _logger.LogDebug("[HmacSigner] VerifySignature: canonical={Canonical} provided={Sig}",
            canonical, providedSignature);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hmacSecret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));

        var providedBytes = ParseSignatureToBytes(providedSignature.Trim());
        if (providedBytes.Length == 0) return false;

        _logger.LogDebug("[HmacSigner] expected(hex)={E} provided(hex)={P}",
            BytesToHex(expectedBytes), BytesToHex(providedBytes));

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    /// <summary>
    /// Verifies a legacy payload-style signature: HMAC over PAYLOAD + NONCE + TIMESTAMP.
    /// </summary>
    public bool VerifyPayloadSignature(
        string payload, string nonce, string timestamp,
        string providedSignature, string hmacSecret)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(providedSignature) || string.IsNullOrWhiteSpace(hmacSecret))
                return false;

            _logger.LogInformation("[HmacSigner] VerifyPayload — secret={Mask} payloadLen={L}",
                MaskSecret(hmacSecret), payload?.Length ?? 0);

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hmacSecret));
            var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{payload}{nonce}{timestamp}"));
            var provided = ParseSignatureToBytes(providedSignature);
            if (provided.Length == 0) return false;

            return CryptographicOperations.FixedTimeEquals(expected, provided);
        }
        catch { return false; }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Computes HMAC-SHA256 over the canonical input and returns lowercase hex.</summary>
    public string ComputeHmacHex(string canonicalInput, string hmacSecret)
    {
        if (string.IsNullOrEmpty(canonicalInput)) return string.Empty;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hmacSecret));
        return BytesToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalInput)));
    }

    /// <summary>Normalises any provided signature (Base64 or hex) to a lowercase hex string.</summary>
    public string NormalizeSignatureToHex(string providedSignature)
    {
        if (string.IsNullOrWhiteSpace(providedSignature)) return string.Empty;
        return BytesToHex(ParseSignatureToBytes(providedSignature));
    }

    /// <summary>
    /// Generates a cryptographically secure random nonce (32 hex chars = 128 bits).
    /// </summary>
    public string GenerateNonce()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "");
    }

    /// <summary>Returns current UTC Unix timestamp in seconds.</summary>
    public string GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    // ── Public Helpers ──────────────────────────────────────────────────────────

    /// <summary>Masks a secret for safe logging: first 4 chars + ellipsis + last 4 chars.</summary>
    public string MaskSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        var s = secret.Trim();
        if (s.Length <= 8) return new string('*', s.Length);
        return s[..4] + "..." + s[^4..];
    }

    // ── Private ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a signature string into raw bytes.
    /// Preference: hex (all hex digits) → standard Base64 fallback.
    /// </summary>
    private static byte[] ParseSignatureToBytes(string sig)
    {
        if (string.IsNullOrEmpty(sig)) return Array.Empty<byte>();
        var s = sig.Trim();

        // Prefer hex when it looks like a hex string (optional 0x, all hex digits, even length)
        var candidate = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        var isHex = candidate.Length > 0
            && candidate.Length % 2 == 0
            && candidate.All(c => Uri.IsHexDigit(c));

        if (isHex)
        {
            try
            {
                var b = new byte[candidate.Length / 2];
                for (var i = 0; i < b.Length; i++)
                    b[i] = Convert.ToByte(candidate.Substring(i * 2, 2), 16);
                return b;
            }
            catch { /* fall through to base64 */ }
        }

        // Fallback: standard base64
        try { return Convert.FromBase64String(s); }
        catch { }

        // Fallback: URL-safe base64
        try
        {
            var safe = s.Replace('-', '+').Replace('_', '/');
            safe = (safe.Length % 4) switch { 2 => safe + "==", 3 => safe + "=", _ => safe };
            return Convert.FromBase64String(safe);
        }
        catch { }

        return Array.Empty<byte>();
    }

    private static string BytesToHex(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
