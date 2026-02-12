using System.Text.Json.Serialization;

namespace PaymentModule.Infrastructure.Gateways.PayHere.DTOs;

/// <summary>
/// Response DTO from PayHere refund API
/// </summary>
public class PayHereRefundResponseDto
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("msg")]
    public string Msg { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public long? Data { get; set; }

    // Helper property to map to our internal domain status
    [JsonIgnore]
    public bool IsSuccess => Status == 1;

    [JsonIgnore]
    public string? ProviderRefundId => Data?.ToString();

    [JsonIgnore]
    public string GlobalStatus => Status == 1 ? "SUCCESS" : "FAILED";

    [JsonIgnore]
    public string? Error => Status == 1 ? null : Msg;
}
