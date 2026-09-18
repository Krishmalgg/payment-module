namespace PaymentModule.Domain.Ports;

/// <summary>
/// Selects a payment gateway adapter at call time rather than at startup.
///
/// This is what replaces the old single-singleton registration: every adapter is
/// registered, and the provider is chosen per request. It is also the seam that a
/// future per-merchant routing model would plug into — resolve by the merchant's
/// configured provider instead of the deployment default.
/// </summary>
public interface IPaymentGatewayResolver
{
    /// <summary>The adapter named by configuration (<c>PaymentGateway:Provider</c>).</summary>
    IPaymentGateway ResolveDefault();

    /// <summary>
    /// The adapter for <paramref name="provider"/>, case-insensitive.
    /// Falls back to the default when null or empty.
    /// </summary>
    /// <exception cref="Exceptions.UnknownProviderException">No adapter is registered for that name.</exception>
    IPaymentGateway Resolve(string? provider);

    /// <summary>True when an adapter is registered for <paramref name="provider"/>.</summary>
    bool IsSupported(string? provider);

    /// <summary>All registered provider keys, for diagnostics and health checks.</summary>
    IReadOnlyCollection<string> SupportedProviders { get; }
}
