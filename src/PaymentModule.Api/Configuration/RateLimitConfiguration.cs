using AspNetCoreRateLimit;
using Microsoft.Extensions.Options;

namespace PaymentModule.Api.Configuration;

public class PaymentRateLimitConfiguration : RateLimitConfiguration
{
    public PaymentRateLimitConfiguration(
        IOptions<IpRateLimitOptions> ipLimitOptions,
        IOptions<ClientRateLimitOptions> clientLimitOptions)
        : base(ipLimitOptions, clientLimitOptions)
    {
    }
}
