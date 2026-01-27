namespace PaymentModule.Application.Features.Payments.Interfaces;

/// <summary>
/// DTO for Payment Status Notification
/// </summary>
public class PaymentStatusPayload
{
    public Guid TransactionId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // PENDING, COMPLETED, FAILED, SUSPICIOUS
    public string? ProviderReference { get; set; }
    public DateTime OccurredAt { get; set; }
}
