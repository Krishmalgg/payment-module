using System.Security.Cryptography;
using System.Text;

namespace PaymentModule.Infrastructure.Gateways.PayHere;

public static class PayHereSecurity
{
    /// <summary>
    /// Generates the MD5 hash for initiating a payment or preapproval.
    /// md5(merchant_id + order_id + amount + currency + md5(merchant_secret))
    /// </summary>
    public static string GenerateHash(string merchantId, string orderId, string amount, string currency, string merchantSecret)
    {
        var secretHash = GetMd5Hash(merchantSecret).ToUpperInvariant();
        var signatureSource = merchantId + orderId + amount + currency + secretHash;
        return GetMd5Hash(signatureSource).ToUpperInvariant();
    }

    /// <summary>
    /// Generates the MD5 hash for initiating a preapproval (Add Card).
    /// hash = to_upper_case(md5(merchant_id + order_id + amount + currency + to_upper_case(md5(merchant_secret))))
    /// If amount is not passed, LKR defaults to 10.00, others to 1.01.
    /// </summary>
    public static string GeneratePreapprovalHash(string merchantId, string orderId, string currency, string amount, string merchantSecret)
    {
        Console.WriteLine($"Generating preapproval hash for merchantId: {merchantId}, orderId: {orderId}, currency: {currency}, amount: {amount}, merchantSecret: {merchantSecret}");
        var secretHash = GetMd5Hash(merchantSecret).ToUpperInvariant();
        Console.WriteLine($"Secret Hash: {secretHash}");
        var signatureSource = merchantId + orderId + amount + currency + secretHash;
        return GetMd5Hash(signatureSource).ToUpperInvariant();
    }

    /// <summary>
    /// Verifies the signature received in a PayHere notification (Webhook).
    /// md5(merchant_id + order_id + payhere_amount + payhere_currency + status_code + md5(merchant_secret))
    /// </summary>
    public static bool VerifyNotificationSignature(
        string merchantId, 
        string orderId, 
        string payhereAmount, 
        string payhereCurrency, 
        string statusCode, 
        string merchantSecret, 
        string remoteSignature)
    {
        var secretHash = GetMd5Hash(merchantSecret).ToUpperInvariant();
        var signatureSource = merchantId + orderId + payhereAmount + payhereCurrency + statusCode + secretHash;
        var localSignature = GetMd5Hash(signatureSource).ToUpperInvariant();

        return string.Equals(localSignature, remoteSignature, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMd5Hash(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }
}
