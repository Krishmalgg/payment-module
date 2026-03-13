using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentModule.Infrastructure.Configuration;

namespace PaymentModule.Infrastructure.Security;

public interface IS2SHeaderGenerator
{
    Dictionary<string, string> GenerateHeaders(string method, string url, string body, string? timestamp = null, string? idempotencyKey = null);
}

public class S2SHeaderGenerator : IS2SHeaderGenerator
{
    private readonly PaperMakerOptions _options;
    private readonly ILogger<S2SHeaderGenerator> _logger;

    public S2SHeaderGenerator(IOptions<PaperMakerOptions> options, ILogger<S2SHeaderGenerator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Dictionary<string, string> GenerateHeaders(string method, string url, string body, string? timestamp = null, string? idempotencyKey = null)
    {
        // 1. Get Active Credentials (use first available or specific NEW/OLD logic if needed)
        // For simplicity, we use the first available key/secret pair.
        // In rotation scenarios, we might want to sign with NEW key.
        var apiKey = _options.ApiKeys?.FirstOrDefault();
        var hmacSecret = _options.HmacSecrets?.FirstOrDefault();

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(hmacSecret))
        {
            _logger.LogWarning("S2S Keys missing. Cannot generate signature.");
            return new Dictionary<string, string>();
        }

        // 2. Generate Timestamp and Nonce
        timestamp ??= DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString();

        // 3. Construct Canonical String
        // Format: {METHOD}{PATH}{TIMESTAMP}{NONCE}{BODY}
        // Note: URL passed here should be path+query (e.g. /api/v1/notifications...)
        var uri = new Uri(url);
        var pathAndQuery = uri.PathAndQuery;

        var canonicalString = $"{method.ToUpper()}{pathAndQuery}{timestamp}{nonce}{body}";
        
        _logger.LogInformation("[S2S] Canonical String: {Canonical}", canonicalString);

        // 4. Compute Signature
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalString));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();

        _logger.LogInformation("[S2S] Generated Signature: {Sig}", signature);

       // (inside GenerateHeaders, after computing `signature` and `pathAndQuery`)
        return new Dictionary<string, string>
        {
            { "x-api-key", apiKey },
            { "x-timestamp", timestamp },
            { "x-nonce", nonce },
            { "x-signature", signature },
            { "x-signed-path", pathAndQuery },
            { "x-idempotency-key", idempotencyKey ?? Guid.NewGuid().ToString() }
        };
    }
}
