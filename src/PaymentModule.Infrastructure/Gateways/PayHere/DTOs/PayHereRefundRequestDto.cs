using System.Text.Json.Serialization;

namespace PaymentModule.Infrastructure.Gateways.PayHere.DTOs;

/// <summary>
/// Request DTO for initiating a refund in PayHere
/// </summary>
public record PayHereRefundRequestDto
{
    [JsonPropertyName("payment_id")]
    public string PaymentId { get; init; } = null!; // Maps to provider_ref_id

    [JsonPropertyName("description")]
    public string Description { get; init; } = null!;

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = null!;
}
