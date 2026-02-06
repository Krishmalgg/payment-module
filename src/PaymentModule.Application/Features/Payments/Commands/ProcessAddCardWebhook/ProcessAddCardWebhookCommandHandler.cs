using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Application.Features.Payments.Commands.ProcessAddCardWebhook;

public record ProcessAddCardWebhookCommand(string Payload) : IRequest<object>;

public class ProcessAddCardWebhookCommandHandler : IRequestHandler<ProcessAddCardWebhookCommand, object>
{
    private readonly IPaymentGateway _gateway;
    private readonly IApplicationDbContext _dbContext;

    public ProcessAddCardWebhookCommandHandler(IPaymentGateway gateway, IApplicationDbContext dbContext)
    {
        _gateway = gateway;
        _dbContext = dbContext;
    }

    public async Task<object> Handle(ProcessAddCardWebhookCommand request, CancellationToken cancellationToken)
    {
        Console.WriteLine("[AddCardWebhook] Processing PayHere preapproval notification...");

        // 1. Verify Signature & Parse via Gateway Adapter
        var result = await _gateway.HandleWebhook(request.Payload, new Dictionary<string, string>(), cancellationToken);
        
        if (!result.IsSuccess && string.IsNullOrEmpty(result.OrderId))
        {
            return new { status = "error", message = result.ErrorMessage ?? "Invalid payload" };
        }

        // 2. Fetch StoredCard record (Use IgnoreQueryFilters because this is a background/system process)
        var storedCard = await _dbContext.StoredCards
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.OrderId == result.OrderId, cancellationToken);

        if (storedCard == null)
        {
            Console.WriteLine($"[AddCardWebhook] ⚠️ StoredCard not found for OrderId: {result.OrderId}");
            return new { status = "error", message = "Record not found" };
        }

        if (!result.IsSuccess)
        {
            Console.WriteLine($"🚩 [SECURITY ALERT] MD5 Signature Mismatch for AddCard Order {result.OrderId}!");
            storedCard.MarkAsFailed();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new { status = "error", message = "Signature mismatch" };
        }

        if (result.StatusCode == "2") // Success
        {
            Console.WriteLine($"[AddCardWebhook] ✅ Card Registered for Order {result.OrderId}. Masked No: {result.CardNo}");
            
            storedCard.Activate(
                result.CustomerToken ?? "", 
                result.CardHolderName ?? "", 
                result.CardNo ?? "", 
                result.CardExpiry ?? "",
                result.CardType ?? "");
        }
        else
        {
            Console.WriteLine($"[AddCardWebhook] ❌ Preapproval failed with status {result.StatusCode} for Order {result.OrderId}");
            storedCard.MarkAsFailed();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new { status = "success" };
    }
}
