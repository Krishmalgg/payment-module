using System;

namespace PaymentModule.Domain.Ports
{
    public interface ICryptoProvider
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }
}
