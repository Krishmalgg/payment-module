using MediatR;

namespace PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;


public record UserData(
    string? UserId,
    string? FullName,
    string? Email,
    string? Address = null,
    string? City = null,
    string? Country = null,
    string? CustomerToken = null
);

public record CreatePaymentIntentCommand(
    decimal Amount,
    string Currency,
    Dictionary<string, string>? Metadata,
    string? IdempotencyKey,
    string? OrderId,
    UserData UserData
) : IRequest<CreatePaymentIntentResponse>;

public record CreatePaymentIntentResponse(
    string TransactionId,
    string Gateway,
    string Action,
    string Url,
    Dictionary<string, string> Fields
);
