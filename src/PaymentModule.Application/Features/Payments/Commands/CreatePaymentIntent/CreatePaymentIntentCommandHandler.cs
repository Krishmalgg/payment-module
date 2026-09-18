using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Application.Common.Mappings;
using PaymentModule.Domain.Entities;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;

public class CreatePaymentIntentCommandHandler
    : IRequestHandler<CreatePaymentIntentCommand, CreatePaymentIntentResponse>
{
    private readonly IPaymentGatewayResolver _gateways;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<CreatePaymentIntentCommandHandler> _logger;

    public CreatePaymentIntentCommandHandler(
        IPaymentGatewayResolver gateways,
        IApplicationDbContext dbContext,
        ILogger<CreatePaymentIntentCommandHandler> logger)
    {
        _gateways = gateways;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CreatePaymentIntentResponse> Handle(
        CreatePaymentIntentCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.OrderId))
            throw new ArgumentException("OrderId is required");

        if (!Guid.TryParse(request.UserData?.UserId, out var userIdGuid))
            throw new ArgumentException("UserId must be a valid GUID");

        var user = request.UserData!;

        // Which provider handles this payment. No hardcoded provider name anywhere below.
        var gateway = _gateways.Resolve(request.Provider);

        // Resolve the stored-card token. Preferring PaymentMethodId means the raw token
        // never has to leave the server, which is why that path exists.
        var token = await ResolvePaymentMethodTokenAsync(user, userIdGuid, cancellationToken);

        var metadata = BuildMetadata(request, user);

        var displayName = user.ResolveDisplayName();
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A customer name is required (FirstName/LastName or FullName)");

        // --- PRE-PERSISTENCE (avoid the webhook-arrives-first race) ---
        var transaction = new Transaction(
            Guid.NewGuid(),
            request.OrderId!,
            userIdGuid,
            request.Amount,
            request.Currency,
            gateway.Provider.ToUpperInvariant(),
            displayName,
            user.Email);

        try
        {
            _dbContext.Transactions.Add(transaction);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Transaction {OrderId} pre-registered as PENDING.", transaction.OrderId);
        }
        catch (DbUpdateException)
        {
            // The webhook may have created the record before we finished this save.
            _logger.LogWarning("Transaction {OrderId} was already registered (likely by webhook).", transaction.OrderId);
        }

        var result = await gateway.CreatePaymentIntent(
            new TransactionId(transaction.Id),
            new Money(request.Amount, request.Currency),
            metadata,
            cancellationToken,
            token);

        _logger.LogInformation(
            "Gateway {Provider} returned action={Action} status={Status} for Order {OrderId}",
            result.Provider, result.Action, result.Status, request.OrderId);

        return new CreatePaymentIntentResponse(
            TransactionId: transaction.Id.ToString(),
            OrderId: transaction.OrderId,
            Provider: result.Provider,
            Action: result.Action.ToWire(),
            Status: result.Status.ToWire(),
            Url: result.Url,
            Fields: result.Fields);
    }

    /// <summary>
    /// Turns a PaymentMethodId into the provider token by looking it up for THIS user.
    /// Scoping the lookup by UserId is what stops one user charging another user's card.
    /// </summary>
    private async Task<string?> ResolvePaymentMethodTokenAsync(
        UserData user,
        Guid userId,
        CancellationToken ct)
    {
        if (user.PaymentMethodId is not { } cardId)
            return user.ResolvedToken;

        var card = await _dbContext.StoredCards
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == cardId && c.UserId == userId && c.Status == "ACTIVE", ct);

        if (card is null)
        {
            throw new ArgumentException(
                $"Stored card {cardId} was not found, is not active, or does not belong to this user.");
        }

        return card.CustomerToken;
    }

    /// <summary>
    /// Builds the neutral metadata bag handed to the adapter. Adapters decide how to
    /// transmit it (PayHere uses custom_1/custom_2; Stripe uses its metadata object).
    /// </summary>
    private static Dictionary<string, string> BuildMetadata(
        CreatePaymentIntentCommand request,
        UserData user)
    {
        var metadata = request.Metadata is not null
            ? new Dictionary<string, string>(request.Metadata)
            : new Dictionary<string, string>();

        var (firstName, lastName) = user.ResolveName();
        var billing = user.ResolveBilling();

        void Set(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) metadata[key] = value;
        }

        Set("order_id", request.OrderId);
        Set("user_id", user.UserId);
        Set("email", user.Email);
        Set("first_name", firstName);
        Set("last_name", lastName);
        Set("address", billing.Line1);
        Set("address_line2", billing.Line2);
        Set("city", billing.City);
        Set("state", billing.State);
        Set("postal_code", billing.PostalCode);
        Set("country", billing.Country);

        return metadata;
    }
}
