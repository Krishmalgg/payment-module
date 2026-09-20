using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentModule.Application.Common.Configuration;
using PaymentModule.Application.Features.Payments.Commands.ProcessWebhook;
using PaymentModule.Application.Features.Payments.Webhooks;
using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;
using PaymentModule.Infrastructure.Gateways;
using Xunit;

namespace PaymentModule.SecurityTests;

/// <summary>
/// Covers the webhook dispatcher: one endpoint must be able to serve providers that
/// post every event type to a single URL, while dedicated per-flow routes keep working.
/// </summary>
public class WebhookDispatchTests
{
    // ── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>Gateway that echoes back whatever the test asks it to "detect", and records the body it saw.</summary>
    private sealed class ScriptedGateway : IPaymentGateway
    {
        private readonly WebhookEventType _detected;
        private readonly bool _signatureValid;

        public string Provider => "scripted";
        public string? LastBodySeen { get; private set; }

        public ScriptedGateway(WebhookEventType detected, bool signatureValid = true)
        {
            _detected = detected;
            _signatureValid = signatureValid;
        }

        public Task<WebhookResult> HandleWebhook(string payload, IDictionary<string, string> headers, CancellationToken ct)
        {
            LastBodySeen = payload;
            return Task.FromResult(new WebhookResult(
                SignatureValid: _signatureValid,
                EventType: _detected,
                Status: PaymentStatus.Completed,
                OrderId: "ORD-1"));
        }

        public Task<PaymentIntentResult> CreatePaymentIntent(
            TransactionId id, Money amount, IReadOnlyDictionary<string, string>? metadata,
            CancellationToken ct, string? paymentMethodToken = null) =>
            throw new NotSupportedException();

        public Task<CardSetupResult> InitiateCardSetup(
            string orderId, IReadOnlyDictionary<string, string>? metadata, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RefundResult> RefundAsync(
            string providerRefId, decimal? amount, string currency, string description, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class SpyHandler : IWebhookEventHandler
    {
        public WebhookEventType EventType { get; }
        public bool WasCalled { get; private set; }

        public SpyHandler(WebhookEventType eventType) => EventType = eventType;

        public Task<object> HandleAsync(WebhookResult result, string provider, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult<object>(new { status = "success", handled = EventType.ToString() });
        }
    }

    private static ProcessWebhookCommandHandler Build(
        IPaymentGateway gateway,
        params IWebhookEventHandler[] handlers)
    {
        var resolver = new PaymentGatewayResolver(
            [gateway],
            Options.Create(new PaymentGatewayOptions { Provider = gateway.Provider }),
            NullLogger<PaymentGatewayResolver>.Instance);

        return new ProcessWebhookCommandHandler(
            resolver, handlers, NullLogger<ProcessWebhookCommandHandler>.Instance);
    }

    // ── Single-endpoint dispatch (Stripe / PayPal shape) ─────────────────────

    [Fact]
    public async Task Routes_a_card_setup_event_arriving_on_the_generic_endpoint()
    {
        // The regression this guards: before dispatch-by-event-type, a setup event on
        // the generic URL was treated as a payment and fabricated a bogus transaction.
        var payment = new SpyHandler(WebhookEventType.Payment);
        var cardSetup = new SpyHandler(WebhookEventType.CardSetup);
        var handler = Build(new ScriptedGateway(WebhookEventType.CardSetup), payment, cardSetup);

        await handler.Handle(new ProcessWebhookCommand("scripted", "body"), CancellationToken.None);

        Assert.True(cardSetup.WasCalled);
        Assert.False(payment.WasCalled);
    }

    [Fact]
    public async Task Routes_a_payment_event_arriving_on_the_generic_endpoint()
    {
        var payment = new SpyHandler(WebhookEventType.Payment);
        var cardSetup = new SpyHandler(WebhookEventType.CardSetup);
        var handler = Build(new ScriptedGateway(WebhookEventType.Payment), payment, cardSetup);

        await handler.Handle(new ProcessWebhookCommand("scripted", "body"), CancellationToken.None);

        Assert.True(payment.WasCalled);
        Assert.False(cardSetup.WasCalled);
    }

    // ── Dedicated-route hint (PayHere shape) ─────────────────────────────────

    [Fact]
    public async Task A_dedicated_route_hint_overrides_payload_detection()
    {
        // A FAILED PayHere preapproval carries no customer_token, so detection reads it
        // as a payment. The /add-card route knows better and must win, or the failure
        // would be applied to a Transaction instead of the StoredCard.
        var payment = new SpyHandler(WebhookEventType.Payment);
        var cardSetup = new SpyHandler(WebhookEventType.CardSetup);
        var handler = Build(new ScriptedGateway(WebhookEventType.Payment), payment, cardSetup);

        await handler.Handle(
            new ProcessWebhookCommand("scripted", "body", null, WebhookEventType.CardSetup),
            CancellationToken.None);

        Assert.True(cardSetup.WasCalled);
        Assert.False(payment.WasCalled);
    }

    // ── Unknown events ───────────────────────────────────────────────────────

    [Fact]
    public async Task Acknowledges_an_unhandled_event_type_without_side_effects()
    {
        // Must NOT fail: Stripe retries rejected webhooks and eventually disables the
        // endpoint. An event we never subscribed to is not an error.
        var payment = new SpyHandler(WebhookEventType.Payment);
        var handler = Build(new ScriptedGateway(WebhookEventType.Refund), payment);

        var result = await handler.Handle(
            new ProcessWebhookCommand("scripted", "body"), CancellationToken.None);

        Assert.False(payment.WasCalled);
        Assert.Contains("ignored", result.ToString());
    }

    [Fact]
    public void Rejects_two_handlers_claiming_the_same_event_type()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Build(new ScriptedGateway(WebhookEventType.Payment),
                  new SpyHandler(WebhookEventType.Payment),
                  new SpyHandler(WebhookEventType.Payment)));

        Assert.Contains("unique", ex.Message);
    }

    // ── Raw body integrity ───────────────────────────────────────────────────

    [Fact]
    public async Task Passes_the_body_to_the_adapter_byte_for_byte()
    {
        // Guards Gap 1: anything that re-encodes the body breaks signature schemes
        // that hash the raw bytes (Stripe, PayPal).
        const string raw = "order_id=ORD-1&name=John+Smith&sig=a%2fb%2BC&dup=x&dup=y";

        var gateway = new ScriptedGateway(WebhookEventType.Payment);
        var handler = Build(gateway, new SpyHandler(WebhookEventType.Payment));

        await handler.Handle(new ProcessWebhookCommand("scripted", raw), CancellationToken.None);

        Assert.Equal(raw, gateway.LastBodySeen);
    }
}
