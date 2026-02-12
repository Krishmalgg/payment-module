namespace PaymentModule.Domain.Entities;

/// <summary>
/// Represents a refund request for a transaction
/// </summary>
public class Refund
{
    public Guid RefundId { get; private set; }
    public string TransactionId { get; private set; } = null!; // Idempotency guard (unique)
    public string? ProviderRefundRef { get; private set; }
    public string Status { get; private set; } = null!; // REQUESTED, PROCESSING, SUCCESS, FAILED
    public string? FailureReason { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }
    public int RetryCount { get; private set; }

    public Refund(
        Guid refundId,
        string transactionId,
        decimal amount,
        string currency)
    {
        RefundId = refundId;
        TransactionId = transactionId;
        Amount = amount;
        Currency = currency;
        Status = RefundStatus.Requested;
        RetryCount = 0;
    }

    public void MarkAsProcessing()
    {
        if (Status == RefundStatus.Requested || Status == RefundStatus.Failed)
        {
            Status = RefundStatus.Processing;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void MarkAsSuccess(string providerRefundRef)
    {
        Status = RefundStatus.Success;
        ProviderRefundRef = providerRefundRef;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsFailed(string failureReason)
    {
        Status = RefundStatus.Failed;
        FailureReason = failureReason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void IncrementRetryCount()
    {
        RetryCount++;
        UpdatedAt = DateTime.UtcNow;
    }

    // Private constructor for EF Core
    private Refund() { }
}

/// <summary>
/// Refund status constants
/// </summary>
public static class RefundStatus
{
    public const string Requested = "REQUESTED";
    public const string Processing = "PROCESSING";
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
}
