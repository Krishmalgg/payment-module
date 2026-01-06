using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;
using PaymentModule.Infrastructure.Gateways;
using PaymentModule.Infrastructure.Configuration;
using Polly;
using Polly.Registry;

namespace PaymentModule.Infrastructure.Gateways.Adapters;

public class PayHereAdapter : IPaymentGateway
{
    private readonly PayHereOptions _options;
    private readonly ResiliencePipeline _pipeline;

    public PayHereAdapter(IOptions<PayHereOptions> options, ResiliencePipelineProvider<string> pipelineProvider)
    {
        _options = options.Value;
        _pipeline = pipelineProvider.GetPipeline("payment-gateway");
    }

    public Task<PaymentIntentResult> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct)
    {
        // Wrap execution with Polly logic if making direct HTTP calls
        // In this specific adapter, we are constructing a form, but we should prepare the pipeline for when we add direct API calls (e.g. status check)
        if (metadata != null)
        {
            Console.WriteLine("Metadata contents:");
            foreach (var kvp in metadata)
            {
                Console.WriteLine($"  Key: {kvp.Key}, Value: {kvp.Value}");
            }
        }
        else
        {
            Console.WriteLine("Metadata is null");
        }

        var orderId = metadata != null && metadata.TryGetValue("order_id", out var metaOrderId) ? metaOrderId : "0";
        var amountStr = amount.Value.ToString("0.00", CultureInfo.InvariantCulture);
        Console.WriteLine("PayHere Adapter: ");
        Console.WriteLine($"Merchant ID: {_options.MerchantId}");
        Console.WriteLine($"Order ID: {orderId}");
        Console.WriteLine($"Amount: {amountStr}");
        Console.WriteLine($"Currency: {amount.Currency}");
        Console.WriteLine($"Merchant Secret: {_options.MerchantSecret}");
        var secretMd5 = MD5.HashData(Encoding.UTF8.GetBytes(_options.MerchantSecret));
        var secretHash = Convert.ToHexString(secretMd5).ToUpperInvariant();

        var signatureSource = _options.MerchantId + orderId + amountStr + amount.Currency + secretHash;
        var md5 = MD5.HashData(Encoding.UTF8.GetBytes(signatureSource));
        var hash = Convert.ToHexString(md5).ToUpperInvariant();

        var fields = new Dictionary<string, string>
        {
            ["merchant_id"] = _options.MerchantId,
            ["return_url"] = _options.ReturnUrl,
            ["cancel_url"] = _options.CancelUrl,
            ["notify_url"] = "https://df7b8ddfdf95.ngrok-free.app/api/v1/webhooks/payhere",
            ["order_id"] = orderId,
            ["items"] = metadata != null && metadata.TryGetValue("items", out var items) ? items : "Payment",
            ["amount"] = amountStr,
            ["currency"] = amount.Currency,
            ["hash"] = hash,
            ["first_name"] = metadata != null && metadata.TryGetValue("first_name", out var fn) ? fn : "",
            ["last_name"] = metadata != null && metadata.TryGetValue("last_name", out var ln) ? ln : "",
            ["email"] = metadata != null && metadata.TryGetValue("email", out var em) ? em : "",
            ["address"] = metadata != null && metadata.TryGetValue("address", out var ad) ? ad : "",
            ["city"] = metadata != null && metadata.TryGetValue("city", out var ci) ? ci : "",
            ["country"] = metadata != null && metadata.TryGetValue("country", out var co) ? co : ""
        };
        Console.WriteLine("PayHere Fields:");
        foreach (var kvp in fields)
        {
            Console.WriteLine($"{kvp.Key}: {kvp.Value}");
        }

        var result = new PaymentIntentResult(
            Gateway: "payhere",
            Action: "form_post",
            Url: _options.CheckoutUrl,
            Fields: fields
        );
        Console.WriteLine("PayHere Result: "+result);
        return Task.FromResult(result);
    }

    public Task<object> HandleWebhook(string payload, IDictionary<string, string> headers, CancellationToken ct)
    {
        Console.WriteLine("[PayHereAdapter] Analyzing Webhook Payload...");
        Console.WriteLine($"  -> Raw Payload: {payload}");
        
        var form = ParseForm(payload);
        Console.WriteLine($"  -> Keys found: {string.Join(", ", form.Keys)}");
        
        var hasMerchantId = form.TryGetValue("merchant_id", out var merchantId);
        var hasOrderId = form.TryGetValue("order_id", out var orderId);
        var hasAmount = form.TryGetValue("payhere_amount", out var amount); // Note: PayHere uses payhere_amount in notify
        var hasCurrency = form.TryGetValue("payhere_currency", out var currency);
        var hasStatus = form.TryGetValue("status_code", out var statusCode);
        var hasSignature = form.TryGetValue("md5sig", out var remoteSig);

        if (!hasMerchantId || !hasOrderId || !hasAmount || !hasCurrency || !hasStatus || !hasSignature)
        {
            Console.WriteLine("  -> ❌ Missing required fields in form payload.");
            return Task.FromResult<object>(new { ok = false });
        }

        Console.WriteLine($"  -> MerchantID: {merchantId}");
        Console.WriteLine($"  -> OrderID: {orderId}");
        Console.WriteLine($"  -> Amount: {amount}");
        Console.WriteLine($"  -> Status: {statusCode}");

        // PayHere Signature Logic:
        // md5sig = Upper(md5(merchant_id + order_id + payhere_amount + payhere_currency + status_code + Upper(md5(merchant_secret))))
        
        var secretMd5 = MD5.HashData(Encoding.UTF8.GetBytes(_options.MerchantSecret));
        var secretHash = Convert.ToHexString(secretMd5).ToUpperInvariant();

        var signatureSource = merchantId + orderId + amount + currency + statusCode + secretHash;
        var md5 = MD5.HashData(Encoding.UTF8.GetBytes(signatureSource));
        var localSig = Convert.ToHexString(md5).ToUpperInvariant();

        var isOk = localSig == remoteSig?.ToUpperInvariant();

        if (isOk)
        {
             Console.WriteLine("  -> ✅ Signature Match!");
        }
        else
        {
             Console.WriteLine($"  -> ❌ Signature Mismatch. Local: {localSig}, Remote: {remoteSig}");
        }

        var result = new Dictionary<string, object>
        {
            ["ok"] = isOk,
            ["orderId"] = orderId,
            ["status"] = statusCode,
            ["amount"] = amount,
            ["currency"] = currency,
            ["paymentId"] = form.TryGetValue("payment_id", out var pid) ? pid : ""
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

