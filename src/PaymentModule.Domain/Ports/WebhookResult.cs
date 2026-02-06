namespace PaymentModule.Domain.Ports;

/// <summary>
/// A strongly-typed result returned by a gateway after parsing a webhook/notification.
/// </summary>
public record WebhookResult(
    bool IsSuccess,
    string OrderId,
    string StatusCode,
    string? ProviderReference = null,
    string? CustomerToken = null,
    string? CardHolderName = null,
    string? CardNo = null,
    string? CardExpiry = null,
    string? Amount = null,
    string? Currency = null,
    string? CardType = null,
    string? Custom1 = null,
    string? Custom2 = null,
    string? ErrorMessage = null
);
