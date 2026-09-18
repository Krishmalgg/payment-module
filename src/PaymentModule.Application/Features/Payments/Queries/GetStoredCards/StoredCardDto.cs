namespace PaymentModule.Application.Features.Payments.Queries.GetStoredCards;

/// <summary>
/// A saved card, as returned to a client.
///
/// SECURITY: <see cref="Token"/> can charge the card. Prefer sending <see cref="Id"/>
/// back as UserData.PaymentMethodId on /payments/initiate so the raw token never needs
/// to reach a browser. The token is still returned for backwards compatibility and
/// should be removed once every client has migrated.
/// </summary>
public class StoredCardDto
{
    public Guid Id { get; set; }
    public string CardHolderName { get; set; } = string.Empty;
    public string CardNo { get; set; } = string.Empty;
    public string CardExpiry { get; set; } = string.Empty;

    /// <summary>Card network — VISA, MASTER, AMEX. (Previously "CardType".)</summary>
    public string Brand { get; set; } = string.Empty;

    /// <summary>Provider token. (Previously "CustomerToken".) Deprecated — use <see cref="Id"/>.</summary>
    public string Token { get; set; } = string.Empty;
}
