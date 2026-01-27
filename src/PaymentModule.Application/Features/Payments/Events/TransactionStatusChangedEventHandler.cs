using MediatR;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Features.Payments.Interfaces;
using PaymentModule.Domain.Events;

namespace PaymentModule.Application.Features.Payments.Events;

public class TransactionStatusChangedEventHandler : INotificationHandler<TransactionStatusChangedEvent>
{
    private readonly IPaymentStatusNotifier _paymentStatusNotifier;
    private readonly ILogger<TransactionStatusChangedEventHandler> _logger;

    public TransactionStatusChangedEventHandler(
        IPaymentStatusNotifier paymentStatusNotifier, 
        ILogger<TransactionStatusChangedEventHandler> logger)
    {
        _paymentStatusNotifier = paymentStatusNotifier;
        _logger = logger;
    }

    public async Task Handle(TransactionStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Domain Event: Transaction {TransactionId} changed status from {OldStatus} to {NewStatus}", 
            notification.TransactionId, notification.OldStatus, notification.NewStatus);

        // Filter: Only trigger for Pending -> Other Status change?
        if (notification.OldStatus != "PENDING")
        {
            return;
        }

        // Use the centralized notifier feature
        // This internally handles serialization and adding to the outbox with the correct type "PaymentStatus"
        await _paymentStatusNotifier.NotifyAsync(new PaymentStatusPayload
        {
            TransactionId = notification.TransactionId,
            OrderId = notification.OrderId,
            Amount = notification.Amount,
            Currency = notification.Currency,
            UserId = notification.UserId.ToString(),
            UserEmail = notification.Email ?? string.Empty,
            FullName = notification.FullName,
            Status = notification.NewStatus,
            ProviderReference = notification.ProviderRefId,
            OccurredAt = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation("✅ Redirected TransactionStatusChanged domain event to IPaymentStatusNotifier for {OrderId}", notification.OrderId);
    }
}
