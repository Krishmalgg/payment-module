using PaymentModule.Domain.Common;
using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Domain.Entities;

public class Wallet : BaseEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Money Balance { get; private set; } = null!;

    public Wallet(Guid tenantId, Money balance)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        Balance = balance;
    }

    public void Credit(Money amount)
    {
        if (amount.Currency != Balance.Currency)
            throw new InvalidOperationException("Currency mismatch");
        
        Balance = new Money(Balance.Value + amount.Value, Balance.Currency);
        MarkAsUpdated();
    }

    public void Debit(Money amount)
    {
        if (amount.Currency != Balance.Currency)
            throw new InvalidOperationException("Currency mismatch");
        
        if (Balance.Value < amount.Value)
            throw new InvalidOperationException("Insufficient balance");
        
        Balance = new Money(Balance.Value - amount.Value, Balance.Currency);
        MarkAsUpdated();
    }

    private Wallet() { }
}

