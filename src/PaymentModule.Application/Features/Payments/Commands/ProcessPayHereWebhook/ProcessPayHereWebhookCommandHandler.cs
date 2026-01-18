using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Ports;
using System.Text.Json;

namespace PaymentModule.Application.Features.Payments.Commands.ProcessPayHereWebhook;

public class ProcessPayHereWebhookCommandHandler : IRequestHandler<ProcessPayHereWebhookCommand, object>
{
    private readonly IPaymentGateway _gateway;
    private readonly IApplicationDbContext _dbContext;
    private readonly IOutboxService _outbox;

    public ProcessPayHereWebhookCommandHandler(
        IPaymentGateway gateway, 
        IApplicationDbContext dbContext,
        IOutboxService outbox)
    {
        _gateway = gateway;
        _dbContext = dbContext;
        _outbox = outbox;
    }

    public async Task<object> Handle(ProcessPayHereWebhookCommand request, CancellationToken ct)
    {
        Console.WriteLine("[WebhookHandler] Processing PayHere notification...");

        // 1. Verify Signature & Parse via Gateway Adapter
        var rawResult = await _gateway.HandleWebhook(request.Payload, new Dictionary<string, string>(), ct);
        
        if (rawResult is not IDictionary<string, object> result)
        {
            Console.WriteLine("[WebhookHandler] ❌ Payload invalid.");
            return new { status = "error", message = "Invalid payload" };
        }

        bool signatureOk = result.TryGetValue("ok", out var okObj) && (bool)okObj;
        string orderId = result["orderId"]?.ToString() ?? "";
        string status = result["status"]?.ToString() ?? ""; 

        // 2. Fetch Transaction
        var transaction = await _dbContext.Transactions
            .FirstOrDefaultAsync(t => t.OrderId == orderId, ct);

        if (transaction == null)
        {
            Console.WriteLine($"[WebhookHandler] ⚠️ Transaction not found for OrderId: {orderId}");
            return new { status = "error", message = "Transaction not found" };
        }

        var previousStatus = transaction.Status;

        if (!signatureOk)
        {
            Console.WriteLine($"🚩 [SECURITY ALERT] MD5 Signature Mismatch for Order {orderId}!");
            transaction.MarkAsSuspicious("MD5 Signature Mismatch");
        }
        else if (status == "2") // Success
        {
            string providerRef = result.TryGetValue("paymentId", out var pid) && pid != null ? pid.ToString()! : "MOCK_REF";
            transaction.MarkAsCompleted(providerRef);
        }
        else
        {
            transaction.MarkAsFailed();
        }

        if (transaction.Status != previousStatus)
        {
            await _dbContext.SaveChangesAsync(ct);
            Console.WriteLine($"[WebhookHandler] Database updated. Transaction ID: {transaction.Id}, Previous: {previousStatus}, New: {transaction.Status}");

            /* 
            // 3. Notify PaperMaker ONLY if Suspicious (DUPLICATE - handled by TransactionStatusChangedEvent)
            if (transaction.Status == "SUSPICIOUS")
            {
                var eventPayload = JsonSerializer.Serialize(new {
                    TransactionId = transaction.Id,
                    OrderId = transaction.OrderId,
                    Amount = transaction.Amount,
                    Currency = transaction.Currency,
                    UserId = transaction.UserId,
                    UserEmail = transaction.Email,
                    FullName = transaction.FullName,
                    Status = "SUSPICIOUS",
                    Reason = "MD5 Signature Mismatch",
                    OccurredAt = DateTime.UtcNow
                });

                await _outbox.AddMessageAsync(
                    type: "SuspiciousActivity",
                    payload: eventPayload,
                    cancellationToken: ct
                );
                Console.WriteLine("[WebhookHandler] 🛡️ Outbox message 'SuspiciousActivity' created for PaperMaker.");
            }
            */
        }
        else
        {
            Console.WriteLine($"[WebhookHandler] No status change for Order {orderId}. Current status: {transaction.Status}");
        }

        return new { status = "success" };
    }
}
