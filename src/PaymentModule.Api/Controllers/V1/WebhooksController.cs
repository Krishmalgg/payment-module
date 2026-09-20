using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentModule.Application.Features.Payments.Commands.ProcessWebhook;
using PaymentModule.Domain.Enums;

namespace PaymentModule.Api.Controllers.V1;

/// <summary>
/// Provider notification endpoints.
///
/// Routes are parameterised by provider, so adding a gateway needs no new endpoint —
/// only a registered adapter and a notify URL pointing here.
///
/// Two shapes are supported:
///   POST /api/v1/webhooks/{provider}            — one endpoint for every event type,
///                                                 dispatched on the parsed payload.
///                                                 (Stripe, PayPal)
///   POST /api/v1/webhooks/{provider}/{flow}     — a route dedicated to one flow, for
///                                                 providers configured with a separate
///                                                 notify URL per flow. (PayHere)
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IMediator mediator, ILogger<WebhooksController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Universal endpoint. The event type is determined from the verified payload,
    /// so a provider may post payments, card setups and refunds all to this one URL.
    /// </summary>
    [HttpPost("{provider}")]
    public Task<IActionResult> Notification(string provider, CancellationToken ct) =>
        DispatchAsync(provider, expected: null, ct);

    /// <summary>Card tokenisation notification. e.g. POST /api/v1/webhooks/payhere/add-card</summary>
    [HttpPost("{provider}/add-card")]
    public Task<IActionResult> CardSetupNotification(string provider, CancellationToken ct) =>
        DispatchAsync(provider, expected: WebhookEventType.CardSetup, ct);

    /// <summary>Notification for a charge against a stored card.</summary>
    [HttpPost("{provider}/instant-payment")]
    public Task<IActionResult> InstantPaymentNotification(string provider, CancellationToken ct) =>
        DispatchAsync(provider, expected: WebhookEventType.Payment, ct);

    private async Task<IActionResult> DispatchAsync(
        string provider,
        WebhookEventType? expected,
        CancellationToken ct)
    {
        _logger.LogInformation("[Webhook] Notification received from {Provider} (expected={Expected})",
            provider, expected?.ToString() ?? "auto-detect");

        var payload = await ReadPayloadAsync(ct);

        var result = await _mediator.Send(
            new ProcessWebhookCommand(provider, payload, CollectHeaders(), expected), ct);

        return Ok(result);
    }

    private Dictionary<string, string> CollectHeaders() =>
        Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the request body EXACTLY as it arrived.
    ///
    /// Deliberately never touches Request.Form: reading that property makes ASP.NET
    /// consume and re-encode the body, which changes the bytes (spaces become %20
    /// instead of '+', key order is not preserved, repeated keys get comma-joined).
    /// Providers such as Stripe and PayPal verify a signature over the raw bytes, so
    /// any re-encoding makes every one of their webhooks fail verification.
    ///
    /// PayHere is unaffected: it posts application/x-www-form-urlencoded, whose raw
    /// body already is "a=b&amp;c=d" — exactly what the adapters parse.
    /// </summary>
    private async Task<string> ReadPayloadAsync(CancellationToken ct)
    {
        // Buffering is normally enabled upstream by RequestBodyLoggingMiddleware, but
        // that middleware swallows its own failures and could be reordered or removed.
        // Enabling it here keeps this method correct on its own.
        Request.EnableBuffering();
        Request.Body.Position = 0;

        using var reader = new StreamReader(
            Request.Body,
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        return body;
    }
}
