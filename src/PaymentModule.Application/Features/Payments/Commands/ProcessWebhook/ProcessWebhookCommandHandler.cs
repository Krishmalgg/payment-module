using MediatR;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Features.Payments.Webhooks;
using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Application.Features.Payments.Commands.ProcessWebhook;

/// <summary>
/// Single entry point for every provider notification.
///
/// Verifies and parses the payload once, then dispatches on the resulting event type
/// rather than on the URL it arrived at. That is what lets providers which post every
/// event to one endpoint (Stripe, PayPal) work alongside providers which use a separate
/// notify URL per flow (PayHere).
/// </summary>
public class ProcessWebhookCommandHandler : IRequestHandler<ProcessWebhookCommand, object>
{
    private readonly IPaymentGatewayResolver _gateways;
    private readonly IReadOnlyDictionary<WebhookEventType, IWebhookEventHandler> _handlers;
    private readonly ILogger<ProcessWebhookCommandHandler> _logger;

    public ProcessWebhookCommandHandler(
        IPaymentGatewayResolver gateways,
        IEnumerable<IWebhookEventHandler> handlers,
        ILogger<ProcessWebhookCommandHandler> logger)
    {
        _gateways = gateways;
        _logger = logger;

        var map = new Dictionary<WebhookEventType, IWebhookEventHandler>();
        foreach (var handler in handlers)
        {
            if (!map.TryAdd(handler.EventType, handler))
                throw new InvalidOperationException(
                    $"Two handlers both claim {handler.EventType}. Event types must be unique.");
        }
        _handlers = map;
    }

    public async Task<object> Handle(ProcessWebhookCommand request, CancellationToken ct)
    {
        var gateway = _gateways.Resolve(request.Provider);

        var result = await gateway.HandleWebhook(
            request.RawBody,
            request.Headers ?? new Dictionary<string, string>(),
            ct);

        if (string.IsNullOrEmpty(result.OrderId))
        {
            _logger.LogError("Webhook from {Provider} could not be parsed: {Error}",
                gateway.Provider, result.ErrorMessage);
            return new { status = "error", message = result.ErrorMessage ?? "Invalid payload" };
        }

        // A dedicated route knows the flow better than the payload does.
        var eventType = request.ExpectedEventType ?? result.EventType;

        _logger.LogInformation(
            "Webhook parsed. Provider={Provider} Event={Event} (detected={Detected}) OrderId={OrderId} " +
            "SignatureValid={Valid} Status={Status} (raw={Raw}) Amount={Amount} {Currency} Ref={Ref}",
            gateway.Provider, eventType, result.EventType, result.OrderId, result.SignatureValid,
            result.Status, result.RawStatus, result.Amount, result.Currency, result.ProviderReference);

        if (!_handlers.TryGetValue(eventType, out var handler))
        {
            // Acknowledge with 200. Providers send event types we do not care about, and
            // rejecting them triggers retries and — for Stripe — eventual endpoint
            // disabling. Ignoring an event we never subscribed to is not an error.
            _logger.LogInformation(
                "No handler for event type {Event} from {Provider} (order {OrderId}). Acknowledging without action.",
                eventType, gateway.Provider, result.OrderId);

            return new { status = "ignored", message = $"Unhandled event type '{eventType}'" };
        }

        return await handler.HandleAsync(result, gateway.Provider, ct);
    }
}
