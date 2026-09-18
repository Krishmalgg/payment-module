using MediatR;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Application.Common.Mappings;
using PaymentModule.Domain.Entities;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Application.Features.Payments.Commands.AddCard;

public record AddCardCommand(
    Guid UserId,
    string? OrderId = null,
    // Optional provider override ("payhere", "mock"). Falls back to the configured default.
    string? Provider = null
) : IRequest<AddCardResponse>;

public class AddCardCommandHandler : IRequestHandler<AddCardCommand, AddCardResponse>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentGatewayResolver _gateways;
    private readonly ILogger<AddCardCommandHandler> _logger;

    public AddCardCommandHandler(
        IApplicationDbContext context,
        IPaymentGatewayResolver gateways,
        ILogger<AddCardCommandHandler> logger)
    {
        _context = context;
        _gateways = gateways;
        _logger = logger;
    }

    public async Task<AddCardResponse> Handle(AddCardCommand request, CancellationToken cancellationToken)
    {
        var gateway = _gateways.Resolve(request.Provider);

        var orderId = !string.IsNullOrWhiteSpace(request.OrderId)
            ? request.OrderId
            : "CARD_" + Guid.NewGuid().ToString("N").ToUpperInvariant()[..12];

        // 1. Record our intent to store a card before talking to the provider,
        //    so the notification has something to attach to when it arrives.
        var storedCard = new StoredCard(Guid.NewGuid(), request.UserId, orderId);
        _context.StoredCards.Add(storedCard);
        await _context.SaveChangesAsync(cancellationToken);

        // 2. Start the provider's tokenisation flow.
        var metadata = new Dictionary<string, string>
        {
            ["user_id"] = request.UserId.ToString()
        };

        var setup = await gateway.InitiateCardSetup(orderId, metadata, cancellationToken);

        _logger.LogInformation(
            "Card setup started with {Provider} for User {UserId}. OrderId: {OrderId}, Action: {Action}",
            setup.Provider, request.UserId, orderId, setup.Action);

        return new AddCardResponse(
            Success: true,
            Provider: setup.Provider,
            Action: setup.Action.ToWire(),
            OrderId: orderId,
            Url: setup.Url,
            Fields: setup.Fields);
    }
}
