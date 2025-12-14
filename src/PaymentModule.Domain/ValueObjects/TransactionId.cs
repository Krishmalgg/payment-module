namespace PaymentModule.Domain.ValueObjects;

public readonly struct TransactionId
{
    public Guid Value { get; }

    private TransactionId(Guid value)
    {
        Value = value;
    }

    public static TransactionId New() => new TransactionId(Guid.NewGuid());
}

