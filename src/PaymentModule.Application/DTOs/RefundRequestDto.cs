using System.Text.Json.Serialization;

namespace PaymentModule.Application.DTOs;

/// <summary>
/// Shared DTO for refund requests (HTTP + RabbitMQ).
/// </summary>
public record RefundRequestDto(
    [property: JsonPropertyName("refund_id")] string? RefundId,
    [property: JsonPropertyName("order_id")] string? OrderId,
    [property: JsonPropertyName("transaction_id")] string? TransactionId,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("user_id")] string? UserId,
    // Omit or send null for a full refund; supply a value for a partial refund.
    [property: JsonPropertyName("amount")] decimal? Amount = null,
    // Optional provider override; falls back to the transaction's own provider.
    [property: JsonPropertyName("provider")] string? Provider = null
);
