using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Entities;
using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Application.Features.Payments.Commands.ProcessGatewayWebhook;

/// <summary>
/// Applies a payment notification from any gateway to our Transaction record.
///
/// This handler knows nothing about any provider: it reads a verified, already-translated
/// <see cref="WebhookResult"/> and acts on the domain status alone.
/// </summary>
public class ProcessGatewayWebhookCommandHandler : IRequestHandler<ProcessGatewayWebhookCommand, object>
{
    private readonly IPaymentGatewayResolver _gateways;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<ProcessGatewayWebhookCommandHandler> _logger;

    public ProcessGatewayWebhookCommandHandler(
        IPaymentGatewayResolver gateways,
        IApplicationDbContext dbContext,
        ILogger<ProcessGatewayWebhookCommandHandler> logger)
    {
        _gateways = gateways;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<object> Handle(ProcessGatewayWebhookCommand request, CancellationToken ct)
    {
        var gateway = _gateways.Resolve(request.Provider);

        var result = await gateway.HandleWebhook(
            request.Payload,
            request.Headers ?? new Dictionary<string, string>(),
            ct);

        if (string.IsNullOrEmpty(result.OrderId))
        {
            _logger.LogError("Webhook from {Provider} could not be parsed: {Error}",
                gateway.Provider, result.ErrorMessage);
            return new { status = "error", message = result.ErrorMessage ?? "Invalid payload" };
        }

        _logger.LogInformation(
            "Webhook parsed. Provider={Provider} OrderId={OrderId} SignatureValid={Valid} Status={Status} (raw={Raw}) Amount={Amount} {Currency} Ref={Ref}",
            gateway.Provider, result.OrderId, result.SignatureValid, result.Status,
            result.RawStatus, result.Amount, result.Currency, result.ProviderReference);

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
                gateway.Provider.ToUpperInvariant(),
                result.Card?.HolderName ?? "Webhook Customer",
                result.Meta.GetValueOrDefault("email"));

            _dbContext.Transactions.Add(transaction);
            isNew = true;
        }

        var previousStatus = transaction.Status;

        if (!result.SignatureValid)
        {
            _logger.LogError("[SECURITY] Signature verification FAILED for {Provider} order {OrderId}.",
                gateway.Provider, result.OrderId);
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
                        gateway.Provider, result.OrderId, result.RawStatus);
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
