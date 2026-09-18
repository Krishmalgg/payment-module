using PaymentModule.Domain.Enums;
using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Domain.Ports;

/// <summary>
/// Provider-neutral result of parsing and verifying a gateway notification.
///
/// NOTE: <see cref="SignatureValid"/> and <see cref="Status"/> are deliberately separate.
/// The previous single <c>IsSuccess</c> flag meant "the signature verified", but read as
/// "the payment succeeded" — two very different things. Keep them apart.
/// </summary>
public record WebhookResult(
    bool SignatureValid,
    WebhookEventType EventType,
    PaymentStatus Status,
    string OrderId,
    string? ProviderReference = null,
    decimal? Amount = null,
    string? Currency = null,
    CardDetails? Card = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? ErrorMessage = null,
    string? RawStatus = null
)
{
    /// <summary>Metadata echoed back by the provider, never null for convenience at call sites.</summary>
    public IReadOnlyDictionary<string, string> Meta =>
        Metadata ?? new Dictionary<string, string>();

    /// <summary>Creates a rejection result for a payload that failed verification or parsing.</summary>
    public static WebhookResult Invalid(string orderId, string error, string? rawStatus = null) =>
        new(false, WebhookEventType.Unknown, PaymentStatus.Unknown, orderId,
            ErrorMessage: error, RawStatus: rawStatus);
}
