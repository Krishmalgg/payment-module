using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Application.DTOs;
using PaymentModule.Domain.Entities;
using PaymentModule.Domain.Ports;
using Polly;
using System.Text.Json;

namespace PaymentModule.Application.Features.Refunds.Commands.ProcessRefund;

public class ProcessRefundCommandHandler : IRequestHandler<ProcessRefundCommand, RefundResultDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<ProcessRefundCommandHandler> _logger;

    public ProcessRefundCommandHandler(
        IApplicationDbContext context,
        IPaymentGateway paymentGateway,
        ILogger<ProcessRefundCommandHandler> logger)
    {
        _context = context;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<RefundResultDto> Handle(ProcessRefundCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing refund for TransactionId: {TransactionId}, RefundId: {RefundId}",
            request.TransactionId, request.RefundId);

        // 1. Idempotency Check - check if refund already exists
        var refundGuidParsed = Guid.TryParse(request.RefundId, out var refundGuid);
        var existingRefund = await _context.Refunds
            .FirstOrDefaultAsync(r =>
                (refundGuidParsed && r.RefundId == refundGuid) ||
                r.TransactionId == request.TransactionId,
                cancellationToken);

        if (existingRefund != null)
        {
            _logger.LogInformation("Refund already exists for Transaction: {TransactionId} with Status: {Status}",
                request.TransactionId, existingRefund.Status);

            if (existingRefund.Status == RefundStatus.Success)
            {
                _logger.LogInformation("Returning SUCCESS for existing refund of Transaction: {TransactionId}", request.TransactionId);
                return new RefundResultDto(
                    IsSuccess: true,
                    RefundId: existingRefund.RefundId.ToString(),
                    Status: existingRefund.Status,
                    ProviderRefundRef: existingRefund.ProviderRefundRef,
                    ErrorMessage: null
                );
            }

            if (existingRefund.Status == RefundStatus.Processing)
            {
                _logger.LogWarning("Refund already in progress for Transaction: {TransactionId}", request.TransactionId);
                return new RefundResultDto(
                    IsSuccess: false,
                    RefundId: existingRefund.RefundId.ToString(),
                    Status: existingRefund.Status,
                    ErrorMessage: "Refund is already in progress"
                );
            }

            // If FAILED or REQUESTED, continue to retry
        }

        // 2. Validate Transaction and Ownership
        Transaction? transaction = null;

        // Priority 1: Search by internal TransactionId (GUID)
        if (Guid.TryParse(request.TransactionId, out var txnId))
        {
             transaction = await _context.Transactions.FindAsync(new object[] { txnId }, cancellationToken);
        }

        // Priority 2: Fallback Search by OrderId
        if (transaction == null && !string.IsNullOrEmpty(request.OrderId))
        {
             _logger.LogInformation("Transaction not found by ID/Ref {TransactionId}, trying fallback to OrderId {OrderId}", request.TransactionId, request.OrderId);
             
             transaction = await _context.Transactions
                .FirstOrDefaultAsync(t => t.OrderId == request.OrderId, cancellationToken);
             
                 // If found via OrderId, we should self-heal the missing ProviderRefId (if available)
             if (transaction != null)
             {
                 // Check mismatched IDs if we have a request.TransactionId that is a valid GUID
                 if (Guid.TryParse(request.TransactionId, out var reqGuidInternal) && transaction.Id != reqGuidInternal)
                 {
                      _logger.LogWarning("Fatal Mismatch: Transaction found by OrderId {OrderId} has ID {FoundId} but Request ID is {RequestId}. Using found transaction.", 
                        transaction.OrderId, transaction.Id, request.TransactionId);
                 }
             }
        }

        if (transaction == null)
        {
            _logger.LogWarning("Transaction not found for TransactionId: {TransactionId}", request.TransactionId);
            return new RefundResultDto(
                IsSuccess: false,
                RefundId: request.RefundId,
                Status: RefundStatus.Failed,
                ErrorMessage: "Transaction not found"
            );
        }

        // Ownership validation
        if (transaction.UserId.ToString() != request.UserId)
        {
            _logger.LogWarning("Ownership validation failed. Transaction UserId: {TransactionUserId}, Request UserId: {RequestUserId}",
                transaction.UserId, request.UserId);
            return new RefundResultDto(
                IsSuccess: false,
                RefundId: request.RefundId,
                Status: RefundStatus.Failed,
                ErrorMessage: "Unauthorized: Transaction does not belong to user"
            );
        }

        if (string.IsNullOrEmpty(transaction.ProviderRefId))
        {
            _logger.LogWarning("Transaction {TransactionId} does not have ProviderRefId", request.TransactionId);
            return new RefundResultDto(
                IsSuccess: false,
                RefundId: request.RefundId,
                Status: RefundStatus.Failed,
                ErrorMessage: "Transaction does not have a provider reference ID"
            );
        }

        // 3. Create or update refund record
        Refund refund;
        if (existingRefund == null)
        {
            refund = new Refund(
                Guid.Parse(request.RefundId),
                transaction.Id.ToString(),
                transaction.Amount,
                transaction.Currency
            );
            _context.Refunds.Add(refund);
        }
        else
        {
            refund = existingRefund;
        }

        refund.MarkAsProcessing();
        await _context.SaveChangesAsync(cancellationToken);

        // 4. Call PayHere with retry logic
        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => retryAttempt switch
                {
                    1 => TimeSpan.Zero,           // Immediate
                    2 => TimeSpan.FromSeconds(10),  // +10s
                    3 => TimeSpan.FromSeconds(60),  // +60s
                    _ => TimeSpan.FromSeconds(60)
                },
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} for refund {RefundId} after {Delay}s due to: {Error}",
                        retryCount, request.RefundId, timeSpan.TotalSeconds, exception.Message);
                    refund.IncrementRetryCount();
                }
            );

        PaymentModule.Domain.ValueObjects.RefundResult? payHereResponse = null;
        Exception? lastException = null;

        try
        {
            payHereResponse = await retryPolicy.ExecuteAsync(async () =>
            {
                _logger.LogInformation("Attempting PayHere refund for ProviderRefId: {ProviderRefId}, Amount: {Amount} {Currency}",
                    transaction.ProviderRefId, transaction.Amount, transaction.Currency);

                var response = await _paymentGateway.RefundAsync(
                    transaction.ProviderRefId!,
                    transaction.Amount,
                    transaction.Currency,
                    request.Reason,
                    cancellationToken);

                _logger.LogInformation("PayHere refund response: IsSuccess={IsSuccess}, Status={Status}, Error={Error}",
                    response.IsSuccess, response.Status, response.ErrorMessage);

                // Throw exception if response indicates failure to trigger retry
                if (!response.IsSuccess && !string.IsNullOrEmpty(response.ErrorMessage))
                {
                    throw new Exception($"PayHere refund failed: {response.ErrorMessage}");
                }

                return response;
            });
        }
        catch (Exception ex)
        {
            lastException = ex;
            _logger.LogError(ex, "All refund attempts failed for RefundId: {RefundId}", request.RefundId);
        }

        // 5. Update refund status based on result
        if (payHereResponse != null && payHereResponse.IsSuccess)
        {
            refund.MarkAsSuccess(payHereResponse.ProviderRefundId!);
            transaction.MarkAsRefunded();
            transaction.MarkAsUpdated();

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Refund successful for RefundId: {RefundId}, ProviderRefundId: {ProviderRefundId}",
                request.RefundId, payHereResponse.ProviderRefundId);

            return new RefundResultDto(
                IsSuccess: true,
                RefundId: request.RefundId,
                Status: RefundStatus.Success,
                ProviderRefundRef: payHereResponse.ProviderRefundId
            );
        }
        else
        {
            // 6. Move to DLQ after max retries
            var failureReason = lastException?.Message ?? payHereResponse?.ErrorMessage ?? "Unknown error";
            refund.MarkAsFailed(failureReason);

            var dlqEntry = new DeadLetterQueue(
                Guid.NewGuid(),
                refund.RefundId,
                JsonSerializer.Serialize(request),
                failureReason,
                refund.RetryCount
            );

            _context.DeadLetterQueues.Add(dlqEntry);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogError("Refund failed and moved to DLQ. RefundId: {RefundId}, Reason: {Reason}",
                request.RefundId, failureReason);

            return new RefundResultDto(
                IsSuccess: false,
                RefundId: request.RefundId,
                Status: RefundStatus.Failed,
                ErrorMessage: failureReason
            );
        }
    }
}
