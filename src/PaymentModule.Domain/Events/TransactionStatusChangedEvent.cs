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
    public Guid UserId { get; }
    public string? Email { get; }
    public string FullName { get; }
    public string? ProviderRefId { get; }

    public TransactionStatusChangedEvent(
        Guid transactionId, 
        string oldStatus, 
        string newStatus, 
        string orderId, 
        decimal amount, 
        string currency,
        Guid userId,
        string? email,
        string fullName,
        string? providerRefId)
    {
        TransactionId = transactionId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        UserId = userId;
        Email = email;
        FullName = fullName;
        ProviderRefId = providerRefId;
    }
}
