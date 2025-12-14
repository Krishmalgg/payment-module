using PaymentModule.Domain.Common;
using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Domain.Entities;

public class Transaction : BaseEntity
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateTime OccurredAt { get; private set; }
    public string Status { get; private set; } = "Pending";
    public string? ExternalTransactionId { get; private set; }

    public Transaction(Guid walletId, Money amount, DateTime occurredAt)
    {
        Id = Guid.NewGuid();
        WalletId = walletId;
        Amount = amount;
        OccurredAt = occurredAt;
    }

    public void MarkAsCompleted(string externalTransactionId)
    {
        Status = "Completed";
        ExternalTransactionId = externalTransactionId;
        MarkAsUpdated();
    }

    public void MarkAsFailed()
    {
        Status = "Failed";
        MarkAsUpdated();
    }

    private Transaction() { }
}

