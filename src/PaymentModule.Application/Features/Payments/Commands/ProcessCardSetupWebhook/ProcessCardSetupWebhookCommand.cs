using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Application.Features.Payments.Commands.ProcessCardSetupWebhook;

/// <summary>
/// A card-tokenisation notification received from any gateway.
/// </summary>
public record ProcessCardSetupWebhookCommand(
    string? Provider,
    string Payload,
    IDictionary<string, string>? Headers = null
) : IRequest<object>;

/// <summary>
/// Activates a StoredCard once the provider confirms tokenisation succeeded.
/// </summary>
public class ProcessCardSetupWebhookCommandHandler
    : IRequestHandler<ProcessCardSetupWebhookCommand, object>
{
    private readonly IPaymentGatewayResolver _gateways;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<ProcessCardSetupWebhookCommandHandler> _logger;

    public ProcessCardSetupWebhookCommandHandler(
        IPaymentGatewayResolver gateways,
        IApplicationDbContext dbContext,
        ILogger<ProcessCardSetupWebhookCommandHandler> logger)
    {
        _gateways = gateways;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<object> Handle(ProcessCardSetupWebhookCommand request, CancellationToken ct)
    {
        var gateway = _gateways.Resolve(request.Provider);

        var result = await gateway.HandleWebhook(
            request.Payload,
            request.Headers ?? new Dictionary<string, string>(),
            ct);

        if (string.IsNullOrEmpty(result.OrderId))
        {
            _logger.LogError("Card setup webhook from {Provider} could not be parsed: {Error}",
                gateway.Provider, result.ErrorMessage);
            return new { status = "error", message = result.ErrorMessage ?? "Invalid payload" };
        }

        var storedCard = await _dbContext.StoredCards
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.OrderId == result.OrderId, ct);

        if (storedCard is null)
        {
            _logger.LogWarning("StoredCard not found for OrderId {OrderId}.", result.OrderId);
            return new { status = "error", message = "Record not found" };
        }

        if (storedCard.Status == "ACTIVE")
        {
            _logger.LogWarning("Duplicate card setup webhook for {OrderId}; already ACTIVE.", result.OrderId);
            return new { status = "success", message = "Card already registered" };
        }

        if (!result.SignatureValid)
        {
            _logger.LogError("[SECURITY] Signature verification FAILED for {Provider} card setup {OrderId}.",
                gateway.Provider, result.OrderId);
            storedCard.MarkAsFailed();
            await _dbContext.SaveChangesAsync(ct);
            return new { status = "error", message = "Signature mismatch" };
        }

        if (result.Status == PaymentStatus.Completed && result.Card?.Token is { Length: > 0 })
        {
            _logger.LogInformation("Card registered for Order {OrderId}. Masked: {Masked}",
                result.OrderId, result.Card.MaskedNumber);

            storedCard.Activate(
                result.Card.Token,
                result.Card.HolderName ?? string.Empty,
                result.Card.MaskedNumber ?? string.Empty,
                result.Card.Expiry ?? string.Empty,
                result.Card.Brand ?? string.Empty);
        }
        else
        {
            _logger.LogWarning("Card setup failed for Order {OrderId}. Status={Status} (raw={Raw})",
                result.OrderId, result.Status, result.RawStatus);
            storedCard.MarkAsFailed();
        }

        await _dbContext.SaveChangesAsync(ct);
        return new { status = "success" };
    }
}
