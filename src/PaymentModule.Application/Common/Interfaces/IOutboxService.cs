namespace PaymentModule.Application.Common.Interfaces;

public interface IOutboxService
{
    Task AddMessageAsync(string type, string payload, CancellationToken cancellationToken = default);
    Task<IEnumerable<Guid>> GetUnprocessedMessageIdsAsync(int batchSize = 10, CancellationToken cancellationToken = default);
    Task ProcessMessageAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default);
}
