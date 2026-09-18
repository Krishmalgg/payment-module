using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Exceptions;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;
using PaymentModule.Infrastructure.Configuration;
using PaymentModule.Infrastructure.Gateways;
using Xunit;

namespace PaymentModule.SecurityTests;

/// <summary>
/// Covers provider routing — the piece that replaced the single hardcoded gateway.
/// </summary>
public class PaymentGatewayResolverTests
{
    private sealed class FakeGateway : IPaymentGateway
    {
        public string Provider { get; }
        public FakeGateway(string provider) => Provider = provider;

        public Task<PaymentIntentResult> CreatePaymentIntent(
            TransactionId id, Money amount, IReadOnlyDictionary<string, string>? metadata,
            CancellationToken ct, string? paymentMethodToken = null) =>
            Task.FromResult(new PaymentIntentResult(
                Provider, CheckoutAction.None, PaymentStatus.Pending, null,
                new Dictionary<string, string>()));

        public Task<CardSetupResult> InitiateCardSetup(
            string orderId, IReadOnlyDictionary<string, string>? metadata, CancellationToken ct) =>
            Task.FromResult(new CardSetupResult(
                Provider, CheckoutAction.None, null, new Dictionary<string, string>()));

        public Task<WebhookResult> HandleWebhook(
            string payload, IDictionary<string, string> headers, CancellationToken ct) =>
            Task.FromResult(WebhookResult.Invalid("", "not implemented"));

        public Task<RefundResult> RefundAsync(
            string providerRefId, decimal? amount, string currency, string description, CancellationToken ct) =>
            Task.FromResult(new RefundResult(true, "COMPLETED", "ref", null));
    }

    private static PaymentGatewayResolver Build(string defaultProvider, params string[] providers) =>
        new(providers.Select(p => (IPaymentGateway)new FakeGateway(p)),
            Options.Create(new PaymentGatewayOptions { Provider = defaultProvider }),
            NullLogger<PaymentGatewayResolver>.Instance);

    [Fact]
    public void Resolves_the_named_provider()
    {
        var resolver = Build("payhere", "payhere", "mock");

        Assert.Equal("mock", resolver.Resolve("mock").Provider);
        Assert.Equal("payhere", resolver.Resolve("payhere").Provider);
    }

    [Theory]
    [InlineData("PayHere")]
    [InlineData("PAYHERE")]
    [InlineData("payhere")]
    public void Provider_lookup_is_case_insensitive(string requested)
    {
        // Transaction.Provider is persisted uppercase, so refunds rely on this.
        var resolver = Build("mock", "payhere", "mock");

        Assert.Equal("payhere", resolver.Resolve(requested).Provider);
    }

    [Fact]
    public void Falls_back_to_the_configured_default_when_no_provider_is_named()
    {
        var resolver = Build("mock", "payhere", "mock");

        Assert.Equal("mock", resolver.Resolve(null).Provider);
        Assert.Equal("mock", resolver.Resolve("").Provider);
        Assert.Equal("mock", resolver.ResolveDefault().Provider);
    }

    [Fact]
    public void Throws_a_descriptive_error_for_an_unregistered_provider()
    {
        var resolver = Build("payhere", "payhere");

        var ex = Assert.Throws<UnknownProviderException>(() => resolver.Resolve("stripe"));

        Assert.Equal("stripe", ex.RequestedProvider);
        Assert.Contains("payhere", ex.Message);
    }

    [Fact]
    public void Rejects_a_default_provider_that_has_no_adapter()
    {
        // This is the failure the old code deferred until the first payment request.
        var ex = Assert.Throws<InvalidOperationException>(() => Build("stripe", "payhere", "mock"));

        Assert.Contains("stripe", ex.Message);
    }

    [Fact]
    public void Rejects_duplicate_provider_names()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build("payhere", "payhere", "payhere"));

        Assert.Contains("unique", ex.Message);
    }
}
