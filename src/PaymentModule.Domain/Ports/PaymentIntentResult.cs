using PaymentModule.Domain.Enums;

namespace PaymentModule.Domain.Ports;

/// <summary>
/// Provider-neutral result of starting a payment.
///
/// Not every field is meaningful for every provider — that is intentional. The
/// <see cref="Action"/> tells the caller which fields to read:
///   FormPost     → <see cref="Url"/> + <see cref="Fields"/>   (PayHere)
///   Redirect     → <see cref="Url"/>                          (PayPal)
///   ClientSecret → <see cref="Fields"/>["client_secret"]      (Stripe)
///   None         → <see cref="Status"/> only                  (instant charge on a saved card)
/// </summary>
public record PaymentIntentResult(
    string Provider,
    CheckoutAction Action,
    PaymentStatus Status,
    string? Url,
    IReadOnlyDictionary<string, string> Fields,
    string? ProviderReference = null
);
