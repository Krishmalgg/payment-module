using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentModule.Domain.Enums;
using PaymentModule.Infrastructure.Configuration;
using PaymentModule.Infrastructure.Gateways.Adapters;
using PaymentModule.Infrastructure.Gateways.PayHere;
using Polly;
using Polly.Registry;
using Xunit;

namespace PaymentModule.SecurityTests;

/// <summary>
/// Covers the translation layer that keeps PayHere's vocabulary inside its adapter:
/// status codes become <see cref="PaymentStatus"/>, custom_1/custom_2 become metadata,
/// and signature validity is reported separately from payment outcome.
/// </summary>
public class PayHereWebhookMappingTests
{
    private const string MerchantId = "1211149";
    private const string MerchantSecret = "MTU3ODM5NTk0MTE1Nzg0MDI1MDQxMTU3ODM5NTk0MQ==";

    private static PayHereAdapter BuildAdapter()
    {
        var options = Options.Create(new PayHereOptions
        {
            MerchantId = MerchantId,
            MerchantSecret = MerchantSecret
        });

        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder("payment-gateway", (builder, _) => builder.AddTimeout(TimeSpan.FromSeconds(30)));

        return new PayHereAdapter(
            options,
            registry,
            new FakeHttpClientFactory(),
            NullLogger<PayHereAdapter>.Instance,
            new MemoryCache(new MemoryCacheOptions()));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    /// <summary>Builds a notification body with a genuine md5sig for the given status code.</summary>
    private static string SignedPayload(string statusCode, params string[] extraPairs)
    {
        const string orderId = "ORD-1001";
        const string amount = "1500.00";
        const string currency = "LKR";

        var sig = PayHereSecurity.GenerateNotificationSignature(
            MerchantId, orderId, amount, currency, statusCode, MerchantSecret);

        var pairs = new List<string>
        {
            $"merchant_id={MerchantId}",
            $"order_id={orderId}",
            $"payhere_amount={amount}",
            $"payhere_currency={currency}",
            $"status_code={statusCode}",
            $"md5sig={sig}"
        };
        pairs.AddRange(extraPairs);
        return string.Join("&", pairs);
    }

    [Theory]
    [InlineData("2", PaymentStatus.Completed)]
    [InlineData("0", PaymentStatus.Pending)]
    [InlineData("-1", PaymentStatus.Cancelled)]
    [InlineData("-2", PaymentStatus.Failed)]
    [InlineData("-3", PaymentStatus.Chargeback)]
    [InlineData("99", PaymentStatus.Unknown)]
    public async Task Maps_payhere_status_codes_to_domain_statuses(string raw, PaymentStatus expected)
    {
        var result = await BuildAdapter().HandleWebhook(
            SignedPayload(raw), new Dictionary<string, string>(), CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.Equal(expected, result.Status);
        Assert.Equal(raw, result.RawStatus);
    }

    [Fact]
    public async Task Reports_an_invalid_signature_without_claiming_failure()
    {
        // SignatureValid and Status are separate concerns: a tampered payload can still
        // claim status "2". The adapter must report the signature as invalid regardless.
        var tampered = SignedPayload("2").Replace("md5sig=", "md5sig=DEADBEEF");

        var result = await BuildAdapter().HandleWebhook(
            tampered, new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.Equal("ORD-1001", result.OrderId);
    }

    [Fact]
    public async Task Unpacks_custom_fields_into_neutral_metadata()
    {
        var userId = Guid.NewGuid().ToString();
        var payload = SignedPayload("2", $"custom_1={userId}", "custom_2=buyer@example.com");

        var result = await BuildAdapter().HandleWebhook(
            payload, new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal(userId, result.Meta["user_id"]);
        Assert.Equal("buyer@example.com", result.Meta["email"]);
    }

    [Fact]
    public async Task Classifies_a_preapproval_notification_as_card_setup()
    {
        var payload = SignedPayload("2",
            "customer_token=TOKEN123", "card_no=************4242",
            "card_expiry=12/30", "card_holder_name=A Buyer", "method=VISA");

        var result = await BuildAdapter().HandleWebhook(
            payload, new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal(WebhookEventType.CardSetup, result.EventType);
        Assert.NotNull(result.Card);
        Assert.Equal("TOKEN123", result.Card!.Token);
        Assert.Equal("VISA", result.Card.Brand);
    }

    [Fact]
    public async Task Classifies_a_payment_notification_as_payment()
    {
        var result = await BuildAdapter().HandleWebhook(
            SignedPayload("2", "payment_id=320027123"),
            new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal(WebhookEventType.Payment, result.EventType);
        Assert.Equal("320027123", result.ProviderReference);
    }

    [Fact]
    public async Task Rejects_a_payload_missing_required_fields()
    {
        var result = await BuildAdapter().HandleWebhook(
            "order_id=ORD-1001", new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.Equal(PaymentStatus.Unknown, result.Status);
        Assert.Contains("Missing", result.ErrorMessage);
    }
}
