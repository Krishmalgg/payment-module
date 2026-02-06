using MediatR;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Application.Features.Payments.Interfaces;
using PaymentModule.Domain.Entities;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Application.Features.Payments.Commands.AddCard;

public record AddCardCommand(
    Guid UserId,
    string? OrderId = null
) : IRequest<AddCardResponse>;

public class AddCardCommandHandler : IRequestHandler<AddCardCommand, AddCardResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentGateway _gateway;

    public AddCardCommandHandler(IApplicationDbContext context, IPaymentGateway gateway)
    {
        _context = context;
        _gateway = gateway;
    }

    public async Task<AddCardResponse> Handle(AddCardCommand request, CancellationToken cancellationToken)
    {
        var orderId = !string.IsNullOrWhiteSpace(request.OrderId) 
            ? request.OrderId 
            : "CARD_" + Guid.NewGuid().ToString("N").ToUpper()[..12];
        
        // 1. Create StoredCard record (Simplified to only UserId and OrderId)
        var storedCard = new StoredCard(
            Guid.NewGuid(), 
            request.UserId, 
            orderId);

        _context.StoredCards.Add(storedCard);
        await _context.SaveChangesAsync(cancellationToken);

        // 2. Wrap metadata for PayHere (Empty for now based on simplified request)
        var metadata = new Dictionary<string, string>();

        // 3. Initiate Preapproval via Gateway
        var intent = await _gateway.InitiatePreapproval(orderId, metadata, cancellationToken);

        // 4. Map to simplified response format
        return new AddCardResponse(
            Success: true,
            MerchantId: intent.Fields.GetValueOrDefault("merchant_id", ""),
            OrderId: intent.Fields.GetValueOrDefault("order_id", ""),
            Currency: intent.Fields.GetValueOrDefault("currency", ""),
            Hash: intent.Fields.GetValueOrDefault("hash", ""),
            NotifyUrl: intent.Fields.GetValueOrDefault("notify_url", ""),
            PreapprovalUrl: intent.Url,
            Amount: intent.Fields.GetValueOrDefault("amount", "")
        );
    }
}
