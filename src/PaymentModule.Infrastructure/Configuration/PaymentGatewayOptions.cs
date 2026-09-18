namespace PaymentModule.Infrastructure.Configuration;

/// <summary>
/// Binds the "PaymentGateway" configuration section.
/// </summary>
public class PaymentGatewayOptions
{
    /// <summary>Provider key used when a request does not name one. Must match an adapter's Provider.</summary>
    public string Provider { get; set; } = "mock";

    /// <summary>ISO 4217 codes this deployment will accept.</summary>
    public string[] AcceptedCurrencies { get; set; } = ["LKR", "USD", "EUR", "GBP", "INR", "AUD", "CAD"];
}
