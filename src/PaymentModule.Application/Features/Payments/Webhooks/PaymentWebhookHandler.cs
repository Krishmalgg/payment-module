using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Entities;
using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Application.Features.Payments.Webhooks;

/// <summary>
/// Applies a payment notification to the Transaction record.
///
/// Knows nothing about any provider: it acts on the domain status alone.
/// </summary>
public class PaymentWebhookHandler : IWebhookEventHandler
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<PaymentWebhookHandler> _logger;

    public WebhookEventType EventType => WebhookEventType.Payment;

    public PaymentWebhookHandler(
        IApplicationDbContext dbContext,
        ILogger<PaymentWebhookHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<object> HandleAsync(WebhookResult result, string provider, CancellationToken ct)
    {
        var transaction = await _dbContext.Transactions
            .FirstOrDefaultAsync(t => t.OrderId == result.OrderId, ct);

        // --- Terminal states are never reopened ---
        if (transaction is not null)
        {
            if (transaction.Status == "COMPLETED")
            {
                _logger.LogWarning(
                    "Duplicate webhook: Transaction {OrderId} already COMPLETED. Ignoring status {Status}.",
                    result.OrderId, result.Status);
                return new { status = "success", message = "Payment Already Completed" };
            }

            if (transaction.Status == "SUSPICIOUS")
            {
                _logger.LogWarning("Transaction {OrderId} is SUSPICIOUS. Ignoring update.", result.OrderId);
                return new { status = "success", message = "Transaction locked as SUSPICIOUS" };
            }
        }

        // An unverified payload must never be able to CREATE a record — that would let
        // anyone who can reach this endpoint populate our database with transactions.
        if (transaction is null && !result.SignatureValid)
        {
            _logger.LogError(
                "[SECURITY] Unverified {Provider} webhook for unknown order {OrderId}. Discarding.",
                provider, result.OrderId);
            return new { status = "error", message = "Signature verification failed" };
        }

        // --- Resiliency fallback: the notification beat our own pre-persistence ---
        var isNew = false;
        if (transaction is null)
        {
            _logger.LogInformation(
                "Transaction not found for OrderId {OrderId}. Creating on-the-fly.", result.OrderId);

            if (!Guid.TryParse(result.Meta.GetValueOrDefault("user_id"), out var userId))
                userId = Guid.NewGuid(); // guest checkout or metadata stripped by the provider

            transaction = new Transaction(
                Guid.NewGuid(),
                result.OrderId,
                userId,
                result.Amount ?? 0m,
                result.Currency ?? "LKR",
                provider.ToUpperInvariant(),
                result.Card?.HolderName ?? "Webhook Customer",
                result.Meta.GetValueOrDefault("email"));

            _dbContext.Transactions.Add(transaction);
            isNew = true;
        }

        var previousStatus = transaction.Status;

        if (!result.SignatureValid)
        {
            _logger.LogError("[SECURITY] Signature verification FAILED for {Provider} order {OrderId}.",
                provider, result.OrderId);
            transaction.MarkAsSuspicious("Webhook signature verification failed");
        }
        else
        {
            switch (result.Status)
            {
                case PaymentStatus.Completed:
                    transaction.MarkAsCompleted(result.ProviderReference ?? string.Empty);
                    break;

                case PaymentStatus.Failed:
                case PaymentStatus.Cancelled:
                case PaymentStatus.Chargeback:
                    transaction.MarkAsFailed();
                    break;

                case PaymentStatus.Refunded:
                    transaction.MarkAsRefunded();
                    break;

                case PaymentStatus.Pending:
                    _logger.LogInformation("Order {OrderId} still pending; awaiting a further notification.",
                        result.OrderId);
                    break;

                default:
                    _logger.LogWarning(
                        "Unmapped status from {Provider} for order {OrderId} (raw={Raw}). Leaving unchanged.",
                        provider, result.OrderId, result.RawStatus);
                    break;
            }
        }

        if (transaction.Status != previousStatus || isNew)
        {
            try
            {
                await _dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("Webhook applied. OrderId={OrderId} Status={Status}",
                    result.OrderId, transaction.Status);
            }
            catch (DbUpdateException)
            {
                // The command handler may have saved the same row concurrently.
                // A provider retry will reconcile if anything was actually missed.
                _logger.LogWarning("Concurrent write for Order {OrderId}; skipping.", result.OrderId);
            }
        }
        else
        {
            _logger.LogInformation("No status change for Order {OrderId}; remains {Status}.",
                result.OrderId, transaction.Status);
        }

        return new { status = "success" };
    }
}
