using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Application.Features.Payments.Webhooks;

/// <summary>
/// Activates a StoredCard once a provider confirms tokenisation succeeded.
/// </summary>
public class CardSetupWebhookHandler : IWebhookEventHandler
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<CardSetupWebhookHandler> _logger;

    public WebhookEventType EventType => WebhookEventType.CardSetup;

    public CardSetupWebhookHandler(
        IApplicationDbContext dbContext,
        ILogger<CardSetupWebhookHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<object> HandleAsync(WebhookResult result, string provider, CancellationToken ct)
    {
        var storedCard = await _dbContext.StoredCards
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.OrderId == result.OrderId, ct);

        if (storedCard is null)
        {
            // Unlike payments there is no create-on-the-fly path here: a card can only
            // be activated against a setup we ourselves started.
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
                provider, result.OrderId);
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
