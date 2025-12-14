using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Domain.Entities;

public class Transaction
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateTime OccurredAt { get; private set; }

    public Transaction(Guid walletId, Money amount, DateTime occurredAt)
    {
        WalletId = walletId;
        Amount = amount;
        OccurredAt = occurredAt;
    }

    private Transaction() { }
}

