using PaymentModule.Infrastructure.Persistence.Converters;
using Xunit;

namespace PaymentModule.SecurityTests;

/// <summary>
/// Covers the converter that actually encrypts stored card data at rest
/// (wired up in SecureDbContext when Security:IsEncryptStoredData is true).
/// </summary>
public class AesEncryptionConverterTests
{
    private const string Key = "THIS_IS_A_SECURE_KEY_32_BYTES_LONG_FOR_AES_256!!";

    private static string Encrypt(string plain) =>
        (string)new AesEncryptionConverter(Key).ConvertToProvider(plain)!;

    private static string Decrypt(string cipher) =>
        (string)new AesEncryptionConverter(Key).ConvertFromProvider(cipher)!;

    [Fact]
    public void Roundtrips_a_value()
    {
        const string original = "4111111111111111";

        var encrypted = Encrypt(original);

        Assert.NotEqual(original, encrypted);
        Assert.Equal(original, Decrypt(encrypted));
    }

    [Fact]
    public void Produces_a_different_ciphertext_each_time()
    {
        // A fresh IV per encryption is what stops identical card numbers
        // being recognisable as identical in the database.
        const string original = "same-value";

        var first = Encrypt(original);
        var second = Encrypt(original);

        Assert.NotEqual(first, second);
        Assert.Equal(original, Decrypt(first));
        Assert.Equal(original, Decrypt(second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("unicode: ✓ £ 日本語")]
    public void Handles_edge_case_inputs(string original)
    {
        Assert.Equal(original, Decrypt(Encrypt(original)));
    }
}
