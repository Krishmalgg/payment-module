namespace PaymentModule.Application.DTOs;

/// <summary>
/// Result DTO for refund operations
/// </summary>
public record RefundResultDto(
    bool IsSuccess,
    string RefundId,
    string Status,
    string? ProviderRefundRef = null,
    string? ErrorMessage = null
);
