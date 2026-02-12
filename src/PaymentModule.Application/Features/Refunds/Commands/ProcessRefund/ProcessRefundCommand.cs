using MediatR;
using PaymentModule.Application.DTOs;

namespace PaymentModule.Application.Features.Refunds.Commands.ProcessRefund;

/// <summary>
/// Command to process a refund request
/// </summary>
public record ProcessRefundCommand(
    string RefundId,
    string OrderId,
    string TransactionId,
    string Reason,
    string UserId
) : IRequest<RefundResultDto>;
