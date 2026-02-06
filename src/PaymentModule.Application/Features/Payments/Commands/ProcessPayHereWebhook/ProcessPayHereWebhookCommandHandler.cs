using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace PaymentModule.Application.Features.Payments.Commands.ProcessPayHereWebhook;

public class ProcessPayHereWebhookCommandHandler : IRequestHandler<ProcessPayHereWebhookCommand, object>
{
    private readonly IPaymentGateway _gateway;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<ProcessPayHereWebhookCommandHandler> _logger;

    public ProcessPayHereWebhookCommandHandler(
        IPaymentGateway gateway, 
        IApplicationDbContext dbContext,
        ILogger<ProcessPayHereWebhookCommandHandler> logger)
    {
        _gateway = gateway;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<object> Handle(ProcessPayHereWebhookCommand request, CancellationToken ct)
    {
        Console.WriteLine("[WebhookHandler] Processing PayHere notification...");

        // 1. Verify Signature & Parse via Gateway Adapter
        Console.WriteLine("wehook payload :"+request.Payload);
        var result = await _gateway.HandleWebhook(request.Payload, new Dictionary<string, string>(), ct);
        
        if (!result.IsSuccess && string.IsNullOrEmpty(result.OrderId))
        {
            _logger.LogError("❌ Webhook Handling Failed. Error: {Error}, Payload: {Payload}", result.ErrorMessage, request.Payload);
            return new { status = "error", message = result.ErrorMessage ?? "Invalid payload" };
        }

        _logger.LogInformation("Webhook Result Parsed: OrderId={OrderId}, Success={IsSuccess}, Status={StatusCode}, Amount={Amount} {Currency}, ProviderRef={Ref}, Custom1={C1}, Custom2={C2}",
            result.OrderId, result.IsSuccess, result.StatusCode, result.Amount, result.Currency, result.ProviderReference, result.Custom1, result.Custom2);


        // 2. Fetch Transaction
        _logger.LogInformation("Searching for Transaction with OrderId: {OrderId}", result.OrderId);
        var transaction = await _dbContext.Transactions
            .FirstOrDefaultAsync(t => t.OrderId == result.OrderId, ct);
        _logger.LogInformation("Transaction lookup result for {OrderId}: {Status}", result.OrderId, transaction == null ? "NULL" : "FOUND");
        // --- IDEMPOTENCY / RACE CONDITION HANDLING ---
        // Step 3: If YES, and already processed, Stop.
        if (transaction != null && (transaction.Status == "COMPLETED" || transaction.Status == "FAILED" || transaction.Status == "SUSPICIOUS"))
        {
            _logger.LogInformation("⏭️ Transaction {OrderId} already has status {Status}. Stopping.", result.OrderId, transaction.Status);
            return new { status = "success", message = "Already processed" };
        }

        // Step 4: If NO, Insert the record (Race condition: Webhook arrived before Command Save)
        bool isNew = false;
        if (transaction == null)
        {
            _logger.LogInformation("➕ Transaction not found for OrderId: {OrderId}. Creating on-the-fly (Resiliency Fallback).", result.OrderId);
            
            // Extract UserId from Custom1 (sent during checkout as metadata)
            if (!Guid.TryParse(result.Custom1, out var userId))
            {
                userId = Guid.NewGuid(); // Fall-safe for guest checkouts or missing metadata
            }

            transaction = new Transaction(
                Guid.NewGuid(),
                result.OrderId,
                userId,
                decimal.Parse(result.Amount ?? "0"),
                result.Currency ?? "LKR",
                "PAYHERE",
                result.CardHolderName ?? "Webhook Customer",
                result.Custom2 // Email
            );
            _dbContext.Transactions.Add(transaction);
            isNew = true;
        }

        var previousStatus = transaction.Status;
        _logger.LogInformation("Processing status update for {OrderId}. Current status: {previousStatus}", result.OrderId, previousStatus);

        if (!result.IsSuccess)
        {
            Console.WriteLine($"🚩 [SECURITY ALERT] MD5 Signature Mismatch for Order {result.OrderId}!");
            transaction.MarkAsSuspicious("MD5 Signature Mismatch");
        }
        else if (result.StatusCode == "2") // Success
        {
            _logger.LogInformation("Marking transaction {OrderId} as COMPLETED.", result.OrderId);
            transaction.MarkAsCompleted(result.ProviderReference ?? "MOCK_REF");
        }
        else
        {
            _logger.LogInformation("Marking transaction {OrderId} as FAILED. StatusCode: {StatusCode}", result.OrderId, result.StatusCode);
            transaction.MarkAsFailed();
        }

        if (transaction.Status != previousStatus || isNew)
        {
            try
            {
                await _dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("✅ Webhook processed successfully. OrderId: {OrderId}, New Status: {Status}", result.OrderId, transaction.Status);
            }
            catch (DbUpdateException)
            {
                // Handle Race Condition: If the CommandHandler finished its SaveChanges at the exact same time
                _logger.LogWarning("⚠️ Race condition detected during SaveChanges for Order {OrderId}. The record likely already exists or was updated. Re-checking...", result.OrderId);
                
                // If it was a new record insertion that failed due to unique index, we can just log and ignore
                // if it was an update that failed due to concurrency, EF usually throws DbUpdateConcurrencyException
                // In this case, we've already done our job or the next webhook retry will handle it.
            }
        }
        else
        {
            _logger.LogInformation("ℹ️ No status change needed for Order {OrderId}. Status remains: {Status}", result.OrderId, transaction.Status);
        }

        return new { status = "success" };
    }
}
