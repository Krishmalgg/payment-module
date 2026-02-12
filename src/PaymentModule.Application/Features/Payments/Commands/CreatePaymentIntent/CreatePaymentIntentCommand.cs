using MediatR;

namespace PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;

public record CreatePaymentIntentCommand(
    decimal Amount,
    string Currency,
    Dictionary<string, string>? Metadata,
    string? IdempotencyKey,
    string? UserId,
    string? UserName,
    string? Email,
    string? OrderId,
    string? Address,
    string? City,
    string? Country,
    string? CustomerToken = null
) : IRequest<CreatePaymentIntentResponse>;

public record CreatePaymentIntentResponse(
    string TransactionId,
    string Gateway,
    string Action,
    string Url,
    Dictionary<string, string> Fields
);
