using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentModule.Application.Features.Payments.Commands.ProcessPayHereWebhook;

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
        Console.WriteLine($"[WebhooksController] Received PayHere Webhook. Content-Type: {Request.ContentType}");
        
        string payload;
        if (Request.HasFormContentType)
        {
            // If it's a form post, reconstruct the payload string from the form collection
            payload = string.Join("&", Request.Form.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value.ToString())}"));
            Console.WriteLine("  -> Captured from Form Content");
        }
        else
        {
            // Fallback to raw body reading
            using var reader = new StreamReader(Request.Body);
            payload = await reader.ReadToEndAsync(ct);
            Console.WriteLine("  -> Captured from Raw Body");
        }
        
        var command = new ProcessPayHereWebhookCommand(payload);
        var result = await _mediator.Send(command, ct);
        
        return Ok(result);
    }
}

