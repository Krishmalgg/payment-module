using MediatR;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;

namespace PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;

public class CreatePaymentIntentCommandHandler : IRequestHandler<CreatePaymentIntentCommand, CreatePaymentIntentResponse>
{
    private readonly IPaymentGateway _paymentGateway;

    public CreatePaymentIntentCommandHandler(IPaymentGateway paymentGateway)
    {
        _paymentGateway = paymentGateway;
    }

    public async Task<CreatePaymentIntentResponse> Handle(CreatePaymentIntentCommand request, CancellationToken cancellationToken)
    {
        var transactionId = TransactionId.New();
        var money = new Money(request.Amount, request.Currency);

        var result = await _paymentGateway.CreatePaymentIntent(
            transactionId,
            money,
            request.Metadata,
            cancellationToken
        );

        // TODO: Save transaction to database (will be added with DbContext setup)
        // TODO: Create outbox message for event publishing

        return new CreatePaymentIntentResponse(
            Gateway: result.Gateway,
            Action: result.Action,
            Url: result.Url,
            Fields: result.Fields
        );
    }
}
