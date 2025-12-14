namespace PaymentModule.Api.Controllers.V1;

using Microsoft.AspNetCore.Mvc;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;

[ApiController]
[Route("api/v1/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentGateway _gateway;

    public PaymentsController(IPaymentGateway gateway)
    {
        _gateway = gateway;
    }

    [HttpPost("intents")]
    public async Task<IActionResult> CreateIntent([FromBody] CreateIntentRequest request, CancellationToken ct)
    {
        var txId = TransactionId.New();
        var result = await _gateway.CreatePaymentIntent(txId, new Money(request.Amount, request.Currency), request.Metadata, ct);
        return Ok(result);
    }
}

public sealed class CreateIntentRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public Dictionary<string, string>? Metadata { get; set; }
}

