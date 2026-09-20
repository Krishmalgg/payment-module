using MediatR;
using PaymentModule.Domain.Enums;

namespace PaymentModule.Application.Features.Payments.Commands.ProcessWebhook;

/// <summary>
/// A notification received from any gateway, on any route.
/// </summary>
/// <param name="Provider">Provider key from the webhook route, e.g. "payhere". Null uses the default.</param>
/// <param name="RawBody">Body EXACTLY as received — never re-encoded, or signature checks break.</param>
/// <param name="Headers">Request headers; some providers sign in a header rather than the body.</param>
/// <param name="ExpectedEventType">
/// Set only by routes that are dedicated to one flow (PayHere uses a separate notify URL
/// per flow). When present it WINS over detection, because a dedicated route knows more
/// than a payload does — a failed card setup carries no card token, so detection alone
/// would misread it as a payment.
/// Providers that post every event to one URL leave this null and rely on detection.
/// </param>
public record ProcessWebhookCommand(
    string? Provider,
    string RawBody,
    IDictionary<string, string>? Headers = null,
    WebhookEventType? ExpectedEventType = null
) : IRequest<object>;
