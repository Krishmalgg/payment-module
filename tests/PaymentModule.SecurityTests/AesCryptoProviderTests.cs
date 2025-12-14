using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using PaymentModule.Infrastructure.Security;
using Xunit;
using Xunit.Abstractions;

namespace PaymentModule.SecurityTests
{
    public class AesCryptoProviderTests
    {
        private readonly ITestOutputHelper _output;

        public AesCryptoProviderTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Encrypt_And_Decrypt_Should_Work()
        {
            // Arrange
            var myConfiguration = new Dictionary<string, string>
            {
                {"Security:EncryptionKey", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="}
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(myConfiguration)
                .Build();

            var provider = new AesCryptoProvider(configuration);
            var originalText = "{\"amount\": 100.00, \"currency\": \"USD\"}";

            // Act
            var encrypted = provider.Encrypt(originalText);
            var decrypted = provider.Decrypt(encrypted);

            // Assert
            Assert.NotEqual(originalText, encrypted);
            Assert.Equal(originalText, decrypted);

            _output.WriteLine($"Original: {originalText}");
            _output.WriteLine($"Encrypted: {encrypted}");
        }
    }
}