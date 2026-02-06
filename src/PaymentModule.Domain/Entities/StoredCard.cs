using PaymentModule.Domain.Common;

namespace PaymentModule.Domain.Entities;

/// <summary>
/// Represents a card stored via PayHere Preapproval.
/// </summary>
public class StoredCard : BaseEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string OrderId { get; private set; } = null!;
    
    // Masked card details for display
    public string? CardHolderName { get; private set; }
    public string? CardNo { get; private set; }
    public string? CardExpiry { get; private set; }
    
    // The secure token used for future payments
    public string? CustomerToken { get; private set; }
    
    public string? CardType { get; private set; }
    
    public string Status { get; private set; } = "PENDING";
    
    public StoredCard(
        Guid id, 
        Guid userId, 
        string orderId)
    {
        Id = id;
        UserId = userId;
        OrderId = orderId;
        Status = "PENDING";
    }

    public void Activate(string customerToken, string cardHolderName, string cardNo, string cardExpiry, string cardType)
    {
        CustomerToken = customerToken;
        CardHolderName = cardHolderName;
        CardNo = cardNo;
        CardExpiry = cardExpiry;
        CardType = cardType;
        Status = "ACTIVE";
        MarkAsUpdated();
    }

    public void MarkAsFailed()
    {
        Status = "FAILED";
        MarkAsUpdated();
    }

    // Private constructor for EF Core
    private StoredCard() { }
}
