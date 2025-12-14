namespace PaymentModule.Api.Controllers.V1;

using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;

[ApiController]
[Asp.Versioning.ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PaymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("intents")]
    public async Task<IActionResult> CreateIntent(
        [FromBody] CreatePaymentIntentCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var commandWithKey = command with { IdempotencyKey = idempotencyKey };
        var result = await _mediator.Send(commandWithKey, ct);
        return Ok(result);
    }

    [HttpGet("success")]
    public IActionResult Success([FromQuery] Guid transactionId)
    {
        return Ok(new { transactionId, status = "success" });
    }

    [HttpGet("cancel")]
    public IActionResult Cancel([FromQuery] Guid transactionId)
    {
        return Ok(new { transactionId, status = "cancel" });
    }
}
