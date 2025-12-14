using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;
using PaymentModule.Infrastructure.Gateways;

namespace PaymentModule.Infrastructure.Gateways.Adapters;

public class PayHereAdapter : IPaymentGateway
{
    private readonly PayHereOptions _options;

    public PayHereAdapter(IOptions<PayHereOptions> options)
    {
        _options = options.Value;
    }

    public Task<PaymentIntentResult> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct)
    {
        var orderId = id.Value.ToString("N");
        var amountStr = amount.Value.ToString("0.00", CultureInfo.InvariantCulture);
        var signatureSource = _options.MerchantId + orderId + amountStr + amount.Currency + _options.MerchantSecret;
        var md5 = MD5.HashData(Encoding.UTF8.GetBytes(signatureSource));
        var hash = Convert.ToHexString(md5).ToLowerInvariant();

        var fields = new Dictionary<string, string>
        {
            ["merchant_id"] = _options.MerchantId,
            ["return_url"] = _options.ReturnUrl,
            ["cancel_url"] = _options.CancelUrl,
            ["notify_url"] = _options.NotifyUrl,
            ["order_id"] = orderId,
            ["items"] = metadata != null && metadata.TryGetValue("items", out var items) ? items : "Payment",
            ["amount"] = amountStr,
            ["currency"] = amount.Currency,
            ["hash"] = hash
        };

        var result = new PaymentIntentResult(
            Gateway: "payhere",
            Action: "form_post",
            Url: _options.CheckoutUrl,
            Fields: fields
        );
        return Task.FromResult(result);
    }

    public Task<object> HandleWebhook(string payload, IDictionary<string, string> headers, CancellationToken ct)
    {
        var form = ParseForm(payload);
        var hasMerchantId = form.TryGetValue("merchant_id", out var merchantId);
        var hasOrderId = form.TryGetValue("order_id", out var orderId);
        var hasAmount = form.TryGetValue("amount", out var amount);
        var hasCurrency = form.TryGetValue("currency", out var currency);
        var hasSignature = form.TryGetValue("md5sig", out var remoteSig);

        var ok = false;
        if (hasMerchantId && hasOrderId && hasAmount && hasCurrency && hasSignature && merchantId == _options.MerchantId)
        {
            var signatureSource = _options.MerchantId + orderId + amount + currency + _options.MerchantSecret;
            var md5 = MD5.HashData(Encoding.UTF8.GetBytes(signatureSource));
            var localSig = Convert.ToHexString(md5).ToLowerInvariant();
            ok = localSig == remoteSig?.ToLowerInvariant();
        }

        var result = new
        {
            ok,
            orderId,
            amount,
            currency
        };
        return Task.FromResult<object>(result);
    }

    private static Dictionary<string, string> ParseForm(string payload)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(payload)) return dict;
        var pairs = payload.Split('&');
        foreach (var p in pairs)
        {
            var kv = p.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0] ?? "");
            var val = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
            if (!string.IsNullOrEmpty(key)) dict[key] = val;
        }
        return dict;
    }
}

