using MediatR;
using PaymentModule.Application.DTOs;

namespace PaymentModule.Application.Features.Refunds.Commands.ProcessRefund;

/// <summary>
/// Command to process a refund request.
/// A null <see cref="Amount"/> means a full refund.
/// </summary>
public record ProcessRefundCommand(
    string? RefundId,
    string? OrderId,
    string? TransactionId,
    string? Reason,
    string? UserId,
    decimal? Amount = null,
    string? Provider = null
) : IRequest<RefundResultDto>;
