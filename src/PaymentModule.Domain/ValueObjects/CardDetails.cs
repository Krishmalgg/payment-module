namespace PaymentModule.Domain.ValueObjects;

/// <summary>
/// Provider-neutral description of a tokenised card.
/// Replaces the loose card_* fields that were previously spread across WebhookResult.
/// </summary>
/// <param name="Token">Opaque provider token used to charge this card again. Never expose to a browser.</param>
/// <param name="HolderName">Cardholder name as supplied by the provider.</param>
/// <param name="MaskedNumber">Masked PAN, e.g. <c>************4242</c>.</param>
/// <param name="Expiry">Expiry as supplied by the provider, typically <c>MM/YY</c>.</param>
/// <param name="Brand">Card network — VISA, MASTER, AMEX. (PayHere sends this as <c>method</c>.)</param>
public record CardDetails(
    string? Token,
    string? HolderName,
    string? MaskedNumber,
    string? Expiry,
    string? Brand
);
