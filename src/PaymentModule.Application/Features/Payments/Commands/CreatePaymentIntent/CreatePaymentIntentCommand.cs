using MediatR;

namespace PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;

/// <summary>
/// Billing address in a shape every major gateway accepts.
/// PayHere uses only City/Country; Stripe and PayPal also want PostalCode/State for AVS.
/// </summary>
public record BillingAddress(
    string? Line1 = null,
    string? Line2 = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null,
    string? Country = null
);

/// <summary>
/// Customer details for a payment.
///
/// Input is deliberately lenient: the deprecated flat fields (FullName, Address, City,
/// Country, CustomerToken) are still accepted so a frontend can migrate incrementally.
/// Prefer FirstName/LastName, Billing and PaymentMethodId in new code.
/// </summary>
public record UserData(
    string? UserId = null,
    string? Email = null,

    // --- Preferred ---
    string? FirstName = null,
    string? LastName = null,
    BillingAddress? Billing = null,

    // Id of a stored card from GET /payments/stored-cards. Preferred over sending a raw token.
    Guid? PaymentMethodId = null,

    // Raw provider token for a stored card. Accepted, but PaymentMethodId is safer.
    string? PaymentMethodToken = null,

    // --- Deprecated, still accepted ---
    string? FullName = null,
    string? Address = null,
    string? City = null,
    string? Country = null,
    string? CustomerToken = null
)
{
    /// <summary>The stored-card token, whichever field name the caller used.</summary>
    public string? ResolvedToken => !string.IsNullOrWhiteSpace(PaymentMethodToken)
        ? PaymentMethodToken
        : CustomerToken;

    /// <summary>
    /// Resolves first/last name. Uses the explicit fields when given; otherwise falls back
    /// to splitting FullName on the LAST space, so "Mary Jane Watson" yields
    /// ("Mary Jane", "Watson") rather than the previous ("Mary", "Jane Watson").
    /// </summary>
    public (string First, string Last) ResolveName()
    {
        if (!string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName))
            return (FirstName?.Trim() ?? string.Empty, LastName?.Trim() ?? string.Empty);

        var full = FullName?.Trim();
        if (string.IsNullOrWhiteSpace(full)) return (string.Empty, string.Empty);

        var idx = full.LastIndexOf(' ');
        return idx < 0
            ? (full, string.Empty)
            : (full[..idx].Trim(), full[(idx + 1)..].Trim());
    }

    /// <summary>Display name for the Transaction record, from whichever fields were supplied.</summary>
    public string ResolveDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(FullName)) return FullName.Trim();
        var (first, last) = ResolveName();
        var joined = $"{first} {last}".Trim();
        return joined;
    }

    /// <summary>Billing address, merging the deprecated flat fields when Billing is absent.</summary>
    public BillingAddress ResolveBilling() => Billing ?? new BillingAddress(
        Line1: Address,
        City: City,
        Country: Country);
}

public record CreatePaymentIntentCommand(
    decimal Amount,
    string Currency,
    Dictionary<string, string>? Metadata,
    string? IdempotencyKey,
    string? OrderId,
    UserData UserData,
    // Optional provider override ("payhere", "mock"). Falls back to the configured default.
    string? Provider = null
) : IRequest<CreatePaymentIntentResponse>;

/// <summary>
/// Provider-neutral checkout instruction.
///
/// A client reads <see cref="Action"/> and does exactly one thing:
///   "form_post"     → build a form from <see cref="Fields"/>, POST it to <see cref="Url"/>
///   "redirect"      → navigate to <see cref="Url"/>
///   "client_secret" → hand Fields["client_secret"] to the provider SDK
///   "none"          → nothing to do; read <see cref="Status"/>
/// </summary>
public record CreatePaymentIntentResponse(
    string TransactionId,
    string OrderId,
    string Provider,
    string Action,
    string Status,
    string? Url,
    IReadOnlyDictionary<string, string> Fields
);
