using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using RabbitMQ.Client;
using PaymentModule.Application.Features.Payments.Commands.AddCard;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;
using PaymentModule.Infrastructure.Communication.Core.Connection;
using PaymentModule.Infrastructure.Communication.Core.Producers;

namespace PaymentModule.Infrastructure.Communication.Features.ReceiveAddCardRequests;

/// <summary>
/// Consumes AddCard requests from RabbitMQ and dispatches them to the application layer.
/// </summary>
public class AddCardConsumer : RabbitMqBaseConsumer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AddCardConsumer> _logger;

    protected override string QueueName => _configuration["Messaging:RabbitMq:Queues:AddCard"] ?? "payment.addcard.requests";

    public AddCardConsumer(
        RabbitMqConnection connection,
        ILogger<AddCardConsumer> logger,
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
            _logger.LogInformation("Processing AddCard request from RabbitMQ");

            var command = JsonSerializer.Deserialize<AddCardCommand>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (command == null)
            {
                _logger.LogWarning("Received null or invalid AddCard request");
                return true; // Don't retry invalid data
            }

            using var scope = _serviceProvider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Trigger the application logic
            var result = await mediator.Send(command);

            var finalResponseQueue = _configuration["Messaging:RabbitMq:Queues:AddCardResponse"]
                ?? "payment.addcard.responses";

            if (!string.IsNullOrEmpty(finalResponseQueue))
            {
                var producer = scope.ServiceProvider.GetRequiredService<RabbitMqProducer>();
                var envelope = new
                {
                    isSuccess = true,
                    payload = new
                    {
                        result.Success,
                        result.MerchantId,
                        result.OrderId,
                        result.Currency,
                        result.Hash,
                        result.NotifyUrl,
                        result.PreapprovalUrl,
                        result.Amount
                    }
                };
                var responsePayload = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                Console.WriteLine($"\n[AddCardConsumer] === RESPONSE MESSAGE ===");
                Console.WriteLine($"Queue: {finalResponseQueue}");
                Console.WriteLine($"CorrelationId: {properties.CorrelationId}");
                Console.WriteLine($"IsSuccess: true");
                Console.WriteLine($"Payload:\n{responsePayload}");
                Console.WriteLine($"=========================\n");

                await producer.SendAsync(finalResponseQueue, responsePayload, CancellationToken.None, properties.CorrelationId);
                _logger.LogInformation(
                    "Successfully sent add card result to response queue: {Queue} (CorrelationId: {CorrelationId})",
                    finalResponseQueue,
                    properties.CorrelationId);
            }
            else
            {
                _logger.LogWarning("AddCard result not returned: No default response queue found.");
            }

            _logger.LogInformation("Successfully initiated preapproval for User {UserId}. OrderId: {OrderId}", command.UserId, result.OrderId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process AddCard request from RabbitMQ");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var producer = scope.ServiceProvider.GetRequiredService<RabbitMqProducer>();
                var finalResponseQueue = _configuration["Messaging:RabbitMq:Queues:AddCardResponse"]
                    ?? "payment.addcard.responses";

                var errorEnvelope = new
                {
                    isSuccess = false,
                    error = ex.Message
                };
                var errorPayload = JsonSerializer.Serialize(errorEnvelope, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                Console.WriteLine($"\n[AddCardConsumer] === ERROR RESPONSE ===");
                Console.WriteLine($"Queue: {finalResponseQueue}");
                Console.WriteLine($"CorrelationId: {properties.CorrelationId}");
                Console.WriteLine($"IsSuccess: false");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"=========================\n");

                await producer.SendAsync(finalResponseQueue, errorPayload, CancellationToken.None, properties.CorrelationId);
            }
            catch (Exception sendEx)
            {
                _logger.LogError(sendEx, "Failed to send add card error response to queue");
            }

            return false; // Retry
        }
    }
}
