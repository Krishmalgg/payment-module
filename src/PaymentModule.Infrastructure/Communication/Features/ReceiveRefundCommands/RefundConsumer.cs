using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using RabbitMQ.Client;
using PaymentModule.Application.Features.Refunds.Commands.ProcessRefund;
using PaymentModule.Application.DTOs;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;
using PaymentModule.Infrastructure.Communication.Core.Connection;
using PaymentModule.Infrastructure.Communication.Core.Producers;

namespace PaymentModule.Infrastructure.Communication.Features.ReceiveRefundCommands;

/// <summary>
/// Consumes Refund requests from RabbitMQ and dispatches them to the application layer.
/// </summary>
public class RefundConsumer : RabbitMqBaseConsumer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RefundConsumer> _logger;

    protected override string QueueName => _configuration["Messaging:RabbitMq:Queues:Refund"] ?? "payment.refund.requests";

    public RefundConsumer(
        RabbitMqConnection connection,
        ILogger<RefundConsumer> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration) : base(connection, logger, serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task<bool> ProcessMessageAsync(string message, IReadOnlyBasicProperties properties)
    {
        try
        {
            _logger.LogInformation("Received Refund message: {Message}", message);

            var request = JsonSerializer.Deserialize<RefundRequestDto>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                _logger.LogWarning("Failed to deserialize RefundRequest from message");
                return true; // Don't retry if message is invalid
            }

            using var scope = _serviceProvider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            _logger.LogInformation("Dispatching Refund for Order: {OrderId}, TransactionId: {TransactionId}", 
                request.OrderId, request.TransactionId);
            
            var command = new ProcessRefundCommand(
                request.RefundId,
                request.OrderId,
                request.TransactionId,
                request.Reason,
                request.UserId
            );

            var result = await mediator.Send(command);

            // Determine where to send the response
            var finalResponseQueue = properties.ReplyTo ?? _configuration["Messaging:RabbitMq:Queues:RefundResponse"];

            if (!string.IsNullOrEmpty(finalResponseQueue))
            {
                var producer = scope.ServiceProvider.GetRequiredService<RabbitMqProducer>();
                
                var responsePayload = JsonSerializer.Serialize(result);
                
                await producer.SendAsync(finalResponseQueue, responsePayload, CancellationToken.None, properties.CorrelationId);
                _logger.LogInformation("Successfully sent refund result to response queue: {Queue} (CorrelationId: {CorrelationId})", 
                    finalResponseQueue, properties.CorrelationId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Refund request from RabbitMQ");
            return false; // Retry
        }
    }
}
