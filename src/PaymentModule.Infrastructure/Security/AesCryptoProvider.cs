using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PaymentModule.Infrastructure.Security;

public interface IAesCryptoProvider
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

public class AesCryptoProvider : IAesCryptoProvider
{
    private readonly string _key;

    public AesCryptoProvider(IConfiguration configuration)
    {
        _key = configuration.GetValue<string>("Security:EncryptionKey") 
               ?? throw new ArgumentException("EncryptionKey is missing in configuration");
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(_key));
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        // Prepend IV to the stream
        ms.Write(aes.IV, 0, aes.IV.Length);
        
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        try 
        {
            var fullCipher = Convert.FromBase64String(cipherText);
            
            using var aes = Aes.Create();
            aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(_key));

            // Extract IV (first 16 bytes for AES-128/256 default block size)
            var iv = new byte[aes.BlockSize / 8];
            Array.Copy(fullCipher, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            
            return sr.ReadToEnd();
        }
        catch
        {
            // Return original if decryption fails
            return cipherText;
        }
    }
}
