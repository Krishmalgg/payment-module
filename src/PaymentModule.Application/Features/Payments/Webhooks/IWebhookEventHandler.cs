using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Application.Features.Payments.Webhooks;

/// <summary>
/// Handles one kind of provider notification.
///
/// Handlers declare the event type they serve, exactly as gateway adapters declare
/// their provider name, so the dispatcher never holds a hardcoded list. Adding
/// refund webhooks later means adding one class and registering it.
///
/// SECURITY CONTRACT — read before writing a new handler:
/// A handler is invoked even when <see cref="WebhookResult.SignatureValid"/> is false,
/// because an unverified notification is still worth recording as suspicious. Every
/// implementation MUST therefore check that flag before making any state change other
/// than marking a record suspicious or failed. Nothing in an unverified payload —
/// including OrderId, amounts and card details — may be treated as true.
/// </summary>
public interface IWebhookEventHandler
{
    /// <summary>The event type this handler serves.</summary>
    WebhookEventType EventType { get; }

    /// <summary>
    /// Applies an already-verified-and-translated notification.
    /// </summary>
    /// <param name="result">Parsed notification. Check SignatureValid before trusting it.</param>
    /// <param name="provider">Canonical provider key, e.g. "payhere".</param>
    Task<object> HandleAsync(WebhookResult result, string provider, CancellationToken ct);
}
