using MediatR;

namespace PaymentModule.Application.Features.Payments.Commands.ProcessGatewayWebhook;

/// <summary>
/// A payment notification received from any gateway.
/// </summary>
/// <param name="Provider">Provider key from the webhook route, e.g. "payhere". Null uses the default.</param>
/// <param name="Payload">Raw notification body, exactly as received.</param>
/// <param name="Headers">Request headers — some providers sign in a header rather than the body.</param>
public record ProcessGatewayWebhookCommand(
    string? Provider,
    string Payload,
    IDictionary<string, string>? Headers = null
) : IRequest<object>;
