using MediatR;

namespace PaymentModule.Application.Features.Payments.Commands.ProcessPayHereWebhook;

public record ProcessPayHereWebhookCommand(string Payload) : IRequest<object>;
