namespace PaymentModule.Domain.ValueObjects;

public class Money
{
    public decimal Value { get; private set; }
    public string Currency { get; private set; } = default!;

    public Money(decimal value, string currency)
    {
        Value = value;
        Currency = currency;
    }

    private Money() { }
}

