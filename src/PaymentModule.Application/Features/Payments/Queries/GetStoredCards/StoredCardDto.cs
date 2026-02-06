namespace PaymentModule.Application.Features.Payments.Queries.GetStoredCards;

public class StoredCardDto
{
    public Guid Id { get; set; }
    public string CardHolderName { get; set; } = string.Empty;
    public string CardNo { get; set; } = string.Empty;
    public string CardExpiry { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string CustomerToken { get; set; } = string.Empty;
}
