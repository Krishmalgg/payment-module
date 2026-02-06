namespace PaymentModule.Application.Features.Payments.Interfaces;

/// <summary>
/// Port (Interface) for notifying external systems about payment status changes.
/// This abstraction allows the Application layer to remain independent of infrastructure details.
/// </summary>
public interface IPaymentStatusNotifier
{
    /// <summary>
    /// Notifies external systems (e.g., PaperMaker) about a payment status change.
    /// This method adds the notification to the Outbox for reliable delivery.
    /// </summary>
    /// <param name="payload">The payment status data to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task NotifyAsync(PaymentStatusPayload payload, CancellationToken cancellationToken = default);
}
