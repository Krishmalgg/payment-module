using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;
using PaymentModule.Application.Features.Payments.Commands.AddCard;
using PaymentModule.Application.Features.Payments.Queries.GetStoredCards;

namespace PaymentModule.Api.Controllers.V1;

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
        // Log the incoming payload to the console
        var payloadJson = JsonSerializer.Serialize(command);
        Console.WriteLine($"[CreateIntent] Incoming payload: {payloadJson}");
        var commandWithKey = command with { IdempotencyKey = idempotencyKey };
        var result = await _mediator.Send(commandWithKey, ct);
        return Ok(result);
    }

    [HttpPost("add-card")]
    public async Task<IActionResult> AddCard([FromBody] AddCardCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpGet("stored-cards/{userId}")]
    public async Task<IActionResult> GetStoredCards(Guid userId)
    {
        var result = await _mediator.Send(new GetStoredCardsQuery(userId));
        Console.WriteLine("result :" + JsonSerializer.Serialize(result));
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
