namespace PaymentModule.Domain.Exceptions;

/// <summary>
/// Thrown when a payment provider is requested for which no adapter is registered.
/// </summary>
public class UnknownProviderException : Exception
{
    public string RequestedProvider { get; }
    public IReadOnlyCollection<string> SupportedProviders { get; }

    public UnknownProviderException(string requestedProvider, IReadOnlyCollection<string> supportedProviders)
        : base($"No payment gateway is registered for provider '{requestedProvider}'. " +
               $"Supported providers: {string.Join(", ", supportedProviders)}.")
    {
        RequestedProvider = requestedProvider;
        SupportedProviders = supportedProviders;
    }
}
