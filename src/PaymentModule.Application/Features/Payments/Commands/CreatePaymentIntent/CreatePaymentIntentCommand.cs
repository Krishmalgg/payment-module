using MediatR;

namespace PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;

public record CreatePaymentIntentCommand(
    decimal Amount,
    string Currency,
    Dictionary<string, string>? Metadata,
    string? IdempotencyKey
) : IRequest<CreatePaymentIntentResponse>;

public record CreatePaymentIntentResponse(
    string Gateway,
    string Action,
    string Url,
    Dictionary<string, string> Fields
);
