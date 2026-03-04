using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;
using PaymentModule.Application.Features.Payments.Commands.AddCard;
using PaymentModule.Application.Features.Payments.Queries.GetStoredCards;
using PaymentModule.Application.DTOs;
using PaymentModule.Application.Features.Refunds.Commands.ProcessRefund;

namespace PaymentModule.Api.Controllers.V1;

[ApiController]
[Asp.Versioning.ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IMediator mediator,
        ILogger<PaymentsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("initiate")]
    public async Task<IActionResult> CreateIntent(
        [FromBody] JsonElement body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        // Support two body shapes:
        // 1) Direct CreatePaymentIntentCommand JSON
        // 2) Envelope JSON that contains a `Payload` property with the command inside
        CreatePaymentIntentCommand? command = null;
        //That line of code configures the JSON deserializer to ignore capital and lowercase letters when matching the JSON properties
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        try
        {
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("Payload", out var payload))
            {
                command = JsonSerializer.Deserialize<CreatePaymentIntentCommand>(payload.GetRawText(), jsonOptions);
            }
            else
            {
                command = JsonSerializer.Deserialize<CreatePaymentIntentCommand>(body.GetRawText(), jsonOptions);
            }
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Invalid request payload", details = ex.Message });
        }

        if (command == null)
        {
            return BadRequest(new { error = "Request body could not be deserialized to CreatePaymentIntentCommand" });
        }

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

    [HttpPost("refund")]
    public async Task<IActionResult> ProcessRefund(
        [FromBody] RefundRequestDto? request,
        CancellationToken ct)
    {
        Console.WriteLine("=== REFUND REQUEST RECEIVED ===");

        if (request == null)
        {
            Console.WriteLine("[PaymentsController] ERROR: Request body is NULL");
            return BadRequest(new { error = "Request body is required" });
        }

        try
        {
            var payloadJson = JsonSerializer.Serialize(request);
            Console.WriteLine($"[PaymentsController] Incoming refund payload: {payloadJson}");
            _logger.LogInformation("Received refund request payload: {Payload}", payloadJson);

            if (string.IsNullOrEmpty(request.RefundId))
            {
                Console.WriteLine("[PaymentsController] ERROR: RefundId is missing");
                return BadRequest(new { error = "refund_id is required" });
            }
            if (string.IsNullOrEmpty(request.TransactionId))
            {
                Console.WriteLine("[PaymentsController] ERROR: TransactionId is missing");
                return BadRequest(new { error = "transaction_id is required" });
            }
            if (string.IsNullOrEmpty(request.UserId))
            {
                Console.WriteLine("[PaymentsController] ERROR: UserId is missing");
                return BadRequest(new { error = "user_id is required" });
            }

            var command = new ProcessRefundCommand(
                request.RefundId,
                request.OrderId,
                request.TransactionId,
                request.Reason,
                request.UserId
            );

            var result = await _mediator.Send(command, ct);

            if (result.IsSuccess)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing refund request");
            return StatusCode(500, new { error = "Internal server error processing refund" });
        }
    }
}
