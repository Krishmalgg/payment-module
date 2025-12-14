using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PaymentModule.Infrastructure.Security
{
    public class HmacSignatureService
    {
        private readonly byte[] _secret;

        public HmacSignatureService(IConfiguration configuration)
        {
            var secret = configuration["Security:HmacSecret"] ?? "default-hmac-secret-do-not-use-prod";
            _secret = Encoding.UTF8.GetBytes(secret);
        }

        public string Sign(string payload)
        {
            using var hmac = new HMACSHA256(_secret);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public bool Verify(string payload, string signature)
        {
            var computed = Sign(payload);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed), 
                Encoding.UTF8.GetBytes(signature));
        }
    }
}