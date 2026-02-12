using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PaymentModule.Application.DTOs;
using PaymentModule.Application.Features.Refunds.Commands.ProcessRefund;

namespace PaymentModule.Api.Controllers.V1;

[ApiController]
[Asp.Versioning.ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
public class RefundController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<RefundController> _logger;

    public RefundController(IMediator mediator, ILogger<RefundController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Process a refund request
    /// </summary>
    /// <param name="request">Refund request details</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Refund result</returns>
    [HttpPost("refund")]
    // TODO: Apply X-API-Key and S2S security attributes here
    // [ServiceFilter(typeof(ApiKeyAuthFilter))]
    // [ServiceFilter(typeof(S2SSecurityFilter))]
    public async Task<IActionResult> ProcessRefund(
        [FromBody] RefundRequestDto request,
        CancellationToken ct)
    {
        try
        {
            var payloadJson = JsonSerializer.Serialize(request);
            _logger.LogInformation("Received refund request payload: {Payload}", payloadJson);

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
