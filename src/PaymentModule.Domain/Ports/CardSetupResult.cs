using PaymentModule.Domain.Enums;

namespace PaymentModule.Domain.Ports;

/// <summary>
/// Provider-neutral result of starting a card tokenisation flow.
/// PayHere calls this "preapproval", Stripe a "SetupIntent", PayPal "vaulting" —
/// the port deliberately uses none of those words.
/// Shape mirrors <see cref="PaymentIntentResult"/> so clients can reuse the same handling.
/// </summary>
public record CardSetupResult(
    string Provider,
    CheckoutAction Action,
    string? Url,
    IReadOnlyDictionary<string, string> Fields
);
