using PaymentModule.Domain.Common;

namespace PaymentModule.Domain.Events;

public class TransactionStatusChangedEvent : DomainEvent
{
    public Guid TransactionId { get; }
    public string OldStatus { get; }
    public string NewStatus { get; }
    public string OrderId { get; }
    public decimal Amount { get; }
    public string Currency { get; }

    public TransactionStatusChangedEvent(Guid transactionId, string oldStatus, string newStatus, string orderId, decimal amount, string currency)
    {
        TransactionId = transactionId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
    }
}
