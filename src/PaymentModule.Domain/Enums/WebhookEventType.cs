namespace PaymentModule.Domain.Enums;

/// <summary>
/// The kind of event a provider notification represents. Lets a single webhook
/// endpoint per provider fan out to the right handler.
/// </summary>
public enum WebhookEventType
{
    Unknown = 0,

    /// <summary>A charge attempt reached a new state.</summary>
    Payment,

    /// <summary>A card was tokenised for future use (preapproval / setup intent / vault).</summary>
    CardSetup,

    /// <summary>A refund reached a new state.</summary>
    Refund
}
