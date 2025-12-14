using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Domain.Entities;

public class Wallet
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Money Balance { get; private set; } = null!;

    public Wallet(Guid tenantId, Money balance)
    {
        TenantId = tenantId;
        Balance = balance;
    }

    private Wallet() { }
}

