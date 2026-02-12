using PaymentModule.Domain.Common;
using PaymentModule.Domain.Events;

namespace PaymentModule.Domain.Entities;

/// <summary>
/// Represents a payment transaction
/// All database column mappings are in TransactionConfiguration.cs
/// </summary>
public class Transaction : BaseEntity
{
    // Database Identity
    public Guid Id { get; private set; }
    
    // Business Identity
    public string OrderId { get; private set; } = null!;
    public Guid UserId { get; private set; }
    
    // Payment Details
    public decimal Amount { get; private set; }
    public string Currency { get; internal set; } = "LKR";
    public string Status { get; internal set; } = "PENDING";
    
    // Provider Information
    public string Provider { get; internal set; } = null!;
    public string? ProviderRefId { get; internal set; }
    
    // Customer Snapshot
    public string FullName { get; private set; } = null!;
    public string? Email { get; private set; }
    
    // Timestamps (CreatedAt, UpdatedAt from BaseEntity)
    public DateTime? CompletedAt { get; internal set; }

    public Transaction(
        Guid id,
        string orderId,
        Guid userId, 
        decimal amount,
        string currency, // No default - must be provided
        string provider, 
        string fullName, 
        string? email = null)
    {
        Id = id;
        OrderId = orderId;
        UserId = userId;
        Amount = amount;
        Currency = currency;
        Provider = provider;
        FullName = fullName;
        Email = email;
        Status = "PENDING";
    }

    public void MarkAsCompleted(string providerRefId)
    {
        if (Status == "COMPLETED") return;

        var oldStatus = Status;
        Status = "COMPLETED";
        ProviderRefId = providerRefId;

        CompletedAt = DateTime.UtcNow;
        AddDomainEvent(new TransactionStatusChangedEvent(Id, oldStatus, Status, OrderId, Amount, Currency, UserId, Email, FullName, ProviderRefId));
        MarkAsUpdated();
    }

    public void MarkAsFailed()
    {
        if (Status == "COMPLETED" || Status == "SUSPICIOUS") return;
        
        var oldStatus = Status;
        Status = "FAILED";
        AddDomainEvent(new TransactionStatusChangedEvent(Id, oldStatus, Status, OrderId, Amount, Currency, UserId, Email, FullName, ProviderRefId));
        MarkAsUpdated();
    }

    public void MarkAsSuspicious(string reason)
    {
        if (Status == "COMPLETED") return;
        
        var oldStatus = Status;
        Status = "SUSPICIOUS";
        // We can use ProviderRefId or a new field to store the reason if needed, 
        // but for now, let's just update the status.
        AddDomainEvent(new TransactionStatusChangedEvent(Id, oldStatus, Status, OrderId, Amount, Currency, UserId, Email, FullName, ProviderRefId));
        MarkAsUpdated();
    }

    public void MarkAsRefunded()
    {
        if (Status == "REFUNDED") return;

        var oldStatus = Status;
        Status = "REFUNDED";
        AddDomainEvent(new TransactionStatusChangedEvent(Id, oldStatus, Status, OrderId, Amount, Currency, UserId, Email, FullName, ProviderRefId));
        MarkAsUpdated();
    }

    // Private constructor for EF Core
    private Transaction() { }
}