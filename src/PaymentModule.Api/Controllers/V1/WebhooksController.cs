using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using PaymentModule.Application.Features.Payments.Commands.ProcessPayHereWebhook;
using PaymentModule.Application.Features.Payments.Commands.ProcessAddCardWebhook;

namespace PaymentModule.Api.Controllers.V1;

[ApiController]
[Route("api/v1/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IMediator _mediator;

    public WebhooksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("payhere")]
    public async Task<IActionResult> PayHereNotify(CancellationToken ct)
    {
        var payload = await ReadPayloadAsync(ct);
        var command = new ProcessPayHereWebhookCommand(payload);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("payhere/add-card")]
    public async Task<IActionResult> PayHereAddCardNotify(CancellationToken ct)
    {
        Console.WriteLine("Add Card Webhook");
        var payload = await ReadPayloadAsync(ct);
        var command = new ProcessAddCardWebhookCommand(payload);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("payhere/instant-payment")]
    public async Task<IActionResult> PayHereInstantPaymentNotify(CancellationToken ct)
    {
        Console.WriteLine("[Webhook] Instant Payment Received");
        var payload = await ReadPayloadAsync(ct);
        // We can reuse the same command as it processes the same format
        var command = new ProcessPayHereWebhookCommand(payload);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

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

