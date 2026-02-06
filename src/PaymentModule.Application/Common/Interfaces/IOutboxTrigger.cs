namespace PaymentModule.Application.Common.Interfaces;

public interface IOutboxTrigger
{
    Task WaitForTriggerAsync(CancellationToken cancellationToken);
    void Trigger();
}
