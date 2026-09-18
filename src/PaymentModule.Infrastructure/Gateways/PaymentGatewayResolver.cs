using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentModule.Domain.Exceptions;
using PaymentModule.Domain.Ports;
using PaymentModule.Infrastructure.Configuration;

namespace PaymentModule.Infrastructure.Gateways;

/// <summary>
/// Resolves a payment gateway adapter by provider name.
///
/// Adapters name themselves via <see cref="IPaymentGateway.Provider"/>, so adding a
/// new provider means registering one class — there is no list to update here.
/// </summary>
public class PaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly IReadOnlyDictionary<string, IPaymentGateway> _gateways;
    private readonly string _defaultProvider;
    private readonly ILogger<PaymentGatewayResolver> _logger;

    public PaymentGatewayResolver(
        IEnumerable<IPaymentGateway> gateways,
        IOptions<PaymentGatewayOptions> options,
        ILogger<PaymentGatewayResolver> logger)
    {
        _logger = logger;

        var map = new Dictionary<string, IPaymentGateway>(StringComparer.OrdinalIgnoreCase);
        foreach (var gateway in gateways)
        {
            if (string.IsNullOrWhiteSpace(gateway.Provider))
                throw new InvalidOperationException(
                    $"{gateway.GetType().Name} does not declare a Provider name.");

            if (!map.TryAdd(gateway.Provider, gateway))
                throw new InvalidOperationException(
                    $"Two adapters both claim the provider name '{gateway.Provider}'. Provider names must be unique.");
        }

        _gateways = map;
        _defaultProvider = options.Value.Provider;

        if (_gateways.Count == 0)
            throw new InvalidOperationException("No IPaymentGateway implementations were registered.");

        if (!_gateways.ContainsKey(_defaultProvider))
            throw new InvalidOperationException(
                $"Configured default provider 'PaymentGateway:Provider={_defaultProvider}' has no registered adapter. " +
                $"Registered: {string.Join(", ", _gateways.Keys)}.");

        _logger.LogInformation(
            "Payment gateways registered: {Providers}. Default: {Default}",
            string.Join(", ", _gateways.Keys), _defaultProvider);
    }

    public IReadOnlyCollection<string> SupportedProviders => _gateways.Keys.ToArray();

    public IPaymentGateway ResolveDefault() => _gateways[_defaultProvider];

    public IPaymentGateway Resolve(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return ResolveDefault();

        if (_gateways.TryGetValue(provider, out var gateway))
            return gateway;

        throw new UnknownProviderException(provider, SupportedProviders);
    }

    public bool IsSupported(string? provider) =>
        !string.IsNullOrWhiteSpace(provider) && _gateways.ContainsKey(provider);
}
