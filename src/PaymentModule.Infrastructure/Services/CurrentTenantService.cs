namespace PaymentModule.Infrastructure.Services;

using PaymentModule.Application.Common.Interfaces;

public class CurrentTenantService : ICurrentTenantService
{
    public Guid TenantId { get; }

    public CurrentTenantService()
    {
        TenantId = Guid.Empty;
    }
}
