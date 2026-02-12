namespace PaymentModule.Domain.ValueObjects;

public record RefundResult(
    bool IsSuccess,
    string Status,
    string? ProviderRefundId,
    string? ErrorMessage
);
