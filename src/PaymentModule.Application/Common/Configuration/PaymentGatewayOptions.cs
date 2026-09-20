namespace PaymentModule.Application.Common.Configuration;

/// <summary>
/// Binds the "PaymentGateway" configuration section.
///
/// Lives in the Application layer because it expresses business policy (which provider
/// and which currencies this deployment accepts), not an infrastructure detail — the
/// request validator needs it and cannot reference Infrastructure.
/// </summary>
public class PaymentGatewayOptions
{
    /// <summary>Provider key used when a request does not name one. Must match an adapter's Provider.</summary>
    public string Provider { get; set; } = "mock";

    /// <summary>ISO 4217 codes this deployment will accept.</summary>
    public string[] AcceptedCurrencies { get; set; } = ["LKR", "USD", "EUR", "GBP", "INR", "AUD", "CAD"];
}
