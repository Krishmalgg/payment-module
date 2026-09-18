using Microsoft.Extensions.Diagnostics.HealthChecks;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Api.HealthChecks;

/// <summary>
/// Reports which gateway adapters are wired up and which one is the default.
/// Resolving the default also surfaces a misconfigured PaymentGateway:Provider
/// as an unhealthy check rather than as a failure on the first real payment.
/// </summary>
public class PaymentGatewayHealthCheck : IHealthCheck
{
    private readonly IPaymentGatewayResolver _gateways;

    public PaymentGatewayHealthCheck(IPaymentGatewayResolver gateways)
    {
        _gateways = gateways;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var defaultGateway = _gateways.ResolveDefault();

            var data = new Dictionary<string, object>
            {
                ["default"] = defaultGateway.Provider,
                ["registered"] = _gateways.SupportedProviders
            };

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Default gateway '{defaultGateway.Provider}'; registered: {string.Join(", ", _gateways.SupportedProviders)}",
                data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Payment gateway configuration is invalid", ex));
        }
    }
}
