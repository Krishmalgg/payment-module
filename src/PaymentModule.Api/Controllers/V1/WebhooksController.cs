using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentModule.Application.Features.Payments.Commands.ProcessGatewayWebhook;
using PaymentModule.Application.Features.Payments.Commands.ProcessCardSetupWebhook;

namespace PaymentModule.Api.Controllers.V1;

/// <summary>
/// Provider notification endpoints.
///
/// Routes are parameterised by provider, so adding a gateway needs no new endpoint —
/// only a registered adapter and a notify URL pointing here.
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

    /// <summary>Payment notification. e.g. POST /api/v1/webhooks/payhere</summary>
    [HttpPost("{provider}")]
    public async Task<IActionResult> PaymentNotification(string provider, CancellationToken ct)
    {
        _logger.LogInformation("[Webhook] Payment notification received from {Provider}", provider);

        var payload = await ReadPayloadAsync(ct);
        var result = await _mediator.Send(
            new ProcessGatewayWebhookCommand(provider, payload, CollectHeaders()), ct);

        return Ok(result);
    }

    /// <summary>Card tokenisation notification. e.g. POST /api/v1/webhooks/payhere/add-card</summary>
    [HttpPost("{provider}/add-card")]
    public async Task<IActionResult> CardSetupNotification(string provider, CancellationToken ct)
    {
        _logger.LogInformation("[Webhook] Card setup notification received from {Provider}", provider);

        var payload = await ReadPayloadAsync(ct);
        var result = await _mediator.Send(
            new ProcessCardSetupWebhookCommand(provider, payload, CollectHeaders()), ct);

        return Ok(result);
    }

    /// <summary>
    /// Notification for a charge against a stored card. Same payload format as a normal
    /// payment notification, so it routes to the same handler.
    /// </summary>
    [HttpPost("{provider}/instant-payment")]
    public async Task<IActionResult> InstantPaymentNotification(string provider, CancellationToken ct)
    {
        _logger.LogInformation("[Webhook] Instant payment notification received from {Provider}", provider);

        var payload = await ReadPayloadAsync(ct);
        var result = await _mediator.Send(
            new ProcessGatewayWebhookCommand(provider, payload, CollectHeaders()), ct);

        return Ok(result);
    }

    private Dictionary<string, string> CollectHeaders() =>
        Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

    private async Task<string> ReadPayloadAsync(CancellationToken ct)
    {
        if (Request.HasFormContentType)
        {
            return string.Join("&", Request.Form.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value.ToString())}"));
        }

        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync(ct);
    }
}
