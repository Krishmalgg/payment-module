using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using RabbitMQ.Client;
using PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;
using PaymentModule.Infrastructure.Communication.Core.Connection;
using PaymentModule.Infrastructure.Communication.Core.Producers;

namespace PaymentModule.Infrastructure.Communication.Features.ReceivePaymentCommands;

/// <summary>
/// Consumes Payment Intent requests from RabbitMQ and dispatches them to the application layer.
/// </summary>
public class PaymentIntentConsumer : RabbitMqBaseConsumer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentIntentConsumer> _logger;

    protected override string QueueName => _configuration["Messaging:RabbitMq:Queues:PaymentIntent"] ?? "payment.intent.requests";

    public PaymentIntentConsumer(
        RabbitMqConnection connection,
        ILogger<PaymentIntentConsumer> logger,
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
            _logger.LogInformation("Received PaymentIntent message: {Message}", message);

            // Standard way: Deserialize RAW command directly
            var command = JsonSerializer.Deserialize<CreatePaymentIntentCommand>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (command == null)
            {
                _logger.LogWarning("Failed to deserialize PaymentIntent from message");
                return true; 
            }

            using var scope = _serviceProvider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            _logger.LogInformation("Dispatching PaymentIntent for Order: {OrderId}", command.OrderId);
            
            // Trigger the application logic
            var result = await mediator.Send(command);

            // Determine where to send the response (dedicated response queue only)
            var finalResponseQueue = _configuration["Messaging:RabbitMq:Queues:PaymentIntentResponse"]
                ?? "payment.initiate.responses";

            if (!string.IsNullOrEmpty(finalResponseQueue))
            {
                var producer = scope.ServiceProvider.GetRequiredService<RabbitMqProducer>();
                
                // Wrap response with isSuccess envelope — matches HTTP response format
                var envelope = new { 
                    isSuccess = true,
                    payload = new {
                        OrderId = command.OrderId, 
                        Gateway = result.Gateway,
                        Action = result.Action,
                        Url = result.Url,
                        Fields = result.Fields
                    }
                };
                var responsePayload = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                // Log response for visibility
                Console.WriteLine($"\n[PaymentIntentConsumer] === RESPONSE MESSAGE ===");
                Console.WriteLine($"Queue: {finalResponseQueue}");
                Console.WriteLine($"CorrelationId: {properties.CorrelationId}");
                Console.WriteLine($"IsSuccess: true");
                Console.WriteLine($"Payload:\n{responsePayload}");
                Console.WriteLine($"=========================\n");
                
                // CRITICAL: Send back the CorrelationId so the Main Server can "link" this response to its request
                await producer.SendAsync(finalResponseQueue, responsePayload, CancellationToken.None, properties.CorrelationId);
                _logger.LogInformation("Successfully sent flattened payment intent result to response queue: {Queue} (CorrelationId: {CorrelationId})", 
                    finalResponseQueue, properties.CorrelationId);
            }
            else 
            {
                _logger.LogWarning("PaymentIntent result not returned: No ReplyTo header and no default response queue found.");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process PaymentIntent request from RabbitMQ");

            // Send error response with isSuccess: false so Main Server knows it failed
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var producer = scope.ServiceProvider.GetRequiredService<RabbitMqProducer>();
                var finalResponseQueue = _configuration["Messaging:RabbitMq:Queues:PaymentIntentResponse"]
                    ?? "payment.initiate.responses";

                var errorEnvelope = new
                {
                    isSuccess = false,
                    error = ex.Message
                };
                var errorPayload = JsonSerializer.Serialize(errorEnvelope, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                Console.WriteLine($"\n[PaymentIntentConsumer] === ERROR RESPONSE ===");
                Console.WriteLine($"Queue: {finalResponseQueue}");
                Console.WriteLine($"CorrelationId: {properties.CorrelationId}");
                Console.WriteLine($"IsSuccess: false");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"=========================\n");

                await producer.SendAsync(finalResponseQueue, errorPayload, CancellationToken.None, properties.CorrelationId);
            }
            catch (Exception sendEx)
            {
                _logger.LogError(sendEx, "Failed to send error response to queue");
            }

            return false; // Retry
        }
    }
}
