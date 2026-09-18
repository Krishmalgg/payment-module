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
        var secretHash = GetMd5Hash(merchantSecret).ToUpperInvariant();
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
        var localSignature = GenerateNotificationSignature(
            merchantId, orderId, payhereAmount, payhereCurrency, statusCode, merchantSecret);

        return FixedTimeEquals(localSignature, remoteSignature);
    }

    /// <summary>
    /// Computes the signature PayHere sends as md5sig on a notification.
    /// md5(merchant_id + order_id + payhere_amount + payhere_currency + status_code + md5(merchant_secret))
    /// </summary>
    public static string GenerateNotificationSignature(
        string merchantId,
        string orderId,
        string payhereAmount,
        string payhereCurrency,
        string statusCode,
        string merchantSecret)
    {
        var secretHash = GetMd5Hash(merchantSecret).ToUpperInvariant();
        var signatureSource = merchantId + orderId + payhereAmount + payhereCurrency + statusCode + secretHash;
        return GetMd5Hash(signatureSource).ToUpperInvariant();
    }

    /// <summary>
    /// Compares two hex signatures without leaking their contents through timing.
    /// </summary>
    private static bool FixedTimeEquals(string expected, string provided)
    {
        if (string.IsNullOrEmpty(provided)) return false;

        var expectedBytes = Encoding.ASCII.GetBytes(expected.ToUpperInvariant());
        var providedBytes = Encoding.ASCII.GetBytes(provided.Trim().ToUpperInvariant());

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static string GetMd5Hash(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }
}
