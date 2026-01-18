using MediatR;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;
using PaymentModule.Domain.Entities;

namespace PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;

public class CreatePaymentIntentCommandHandler : IRequestHandler<CreatePaymentIntentCommand, CreatePaymentIntentResponse>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IApplicationDbContext _dbContext;
    private readonly IOutboxService _outbox;

    public CreatePaymentIntentCommandHandler(
        IPaymentGateway paymentGateway, 
        IApplicationDbContext dbContext,
        IOutboxService outbox)
    {
        _paymentGateway = paymentGateway;
        _dbContext = dbContext;
        _outbox = outbox;
    }

    public async Task<CreatePaymentIntentResponse> Handle(CreatePaymentIntentCommand request, CancellationToken cancellationToken)
    {
        // Initialize metadata from the request or create new
        var metadata = request.Metadata != null 
            ? new Dictionary<string, string>(request.Metadata) 
            : new Dictionary<string, string>();

        // Add standard fields for Gateway
        if (!string.IsNullOrEmpty(request.Email)) metadata["email"] = request.Email;
        if (!string.IsNullOrEmpty(request.OrderId)) metadata["order_id"] = request.OrderId;
        if (!string.IsNullOrEmpty(request.Address)) metadata["address"] = request.Address;
        if (!string.IsNullOrEmpty(request.City)) metadata["city"] = request.City;
        if (!string.IsNullOrEmpty(request.Country)) metadata["country"] = request.Country;
        if (!string.IsNullOrEmpty(request.UserName)) 
        {
            var names = request.UserName.Split(' ', 2);
            metadata["first_name"] = names[0];
            if (names.Length > 1) metadata["last_name"] = names[1];
        }

        Console.WriteLine("[CommandHandler] Final Metadata for Gateway:");
        foreach (var item in metadata)
        {
            Console.WriteLine($"  -> {item.Key}: {item.Value}");
        }

        // Validate OrderId
        if (string.IsNullOrEmpty(request.OrderId))
        {
            throw new ArgumentException("OrderId is required");
        }

        // Parse UserId as Guid
        if (!Guid.TryParse(request.UserId, out var userIdGuid))
        {
            throw new ArgumentException("UserId must be a valid GUID");
        }

        // Create Money VO for PaymentGateway
        var money = new Money(request.Amount, request.Currency);
        
        // Try to parse OrderId as Guid for the Internal Gateway VO
        // If it's not a Guid, we generate a new one, but for PayHere we usually want the business OrderId
        var transactionIdGuid = Guid.NewGuid(); // Fallback


        // Call gateway to get fields and hash
        var result = await _paymentGateway.CreatePaymentIntent(
            new TransactionId(transactionIdGuid), 
            money,
            metadata,
            cancellationToken
        );
        foreach (var field in result.Fields)
        {
             Console.WriteLine($"  -> {field.Key}: {field.Value}");
        }

        // Create Transaction with provider from gateway result
        // Use the actual OrderId generated for the gateway to ensure consistency during webhook lookups
        var transaction = new Transaction(
            transactionIdGuid,
            result.Fields.ContainsKey("order_id") ? result.Fields["order_id"] : request.OrderId!,
            userIdGuid,
            request.Amount,
            request.Currency,
            result.Gateway.ToUpperInvariant(),
            request.UserName ?? throw new ArgumentException("UserName is required"),
            request.Email
        );
        
        _dbContext.Transactions.Add(transaction);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreatePaymentIntentResponse(
            Gateway: result.Gateway,
            Action: result.Action,
            Url: result.Url,
            Fields: result.Fields
        );
    }
}
