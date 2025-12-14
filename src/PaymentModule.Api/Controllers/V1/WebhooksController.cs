namespace PaymentModule.Api.Controllers.V1;

using Microsoft.AspNetCore.Mvc;
using PaymentModule.Domain.Ports;

[ApiController]
[Route("api/v1/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IPaymentGateway _gateway;

    public WebhooksController(IPaymentGateway gateway)
    {
        _gateway = gateway;
    }

    [HttpPost]
    public async Task<IActionResult> Notify(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
        var result = await _gateway.HandleWebhook(payload, headers, ct);
        return Ok(result);
    }
}

