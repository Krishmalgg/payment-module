using Microsoft.Extensions.Diagnostics.HealthChecks;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Api.HealthChecks;

public class PaymentGatewayHealthCheck : IHealthCheck
{
    private readonly IPaymentGateway _paymentGateway;

    public PaymentGatewayHealthCheck(IPaymentGateway paymentGateway)
    {
        _paymentGateway = paymentGateway;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // For mock adapter, always healthy
            // For real adapters, could ping their API
            return await Task.FromResult(HealthCheckResult.Healthy("Payment gateway is responsive"));
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Payment gateway is not responsive", ex);
        }
    }
}
