using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Communication.Core.Outbox;

/// <summary>
/// Base abstraction for outbox message processing strategies.
/// Supports different scaling levels: Sequential (Level 1), Parallel (Level 2), Distributed Multi-threaded (Level 3)
/// </summary>
public interface IOutboxProcessingStrategy
{
    int ScaleLevel { get; }
    Task<int> ProcessMessagesAsync(
        IEnumerable<Guid> messageIds,
        IOutboxService outboxService,
        IOutboxDispatcher dispatcher,
        CancellationToken cancellationToken);
}

