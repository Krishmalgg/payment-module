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
        IConfiguration configuration) : base(connection, logger)
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

            // 3. Determine where to send the response (Standard Reply-To Header)
            var finalResponseQueue = properties.ReplyTo 
                ?? _configuration["Messaging:RabbitMq:Queues:GetStoredCardsResponse"];

            if (!string.IsNullOrEmpty(finalResponseQueue))
            {
                var producer = scope.ServiceProvider.GetRequiredService<RabbitMqProducer>();
                
                // Flatten the response as requested
                var responsePayload = JsonSerializer.Serialize(new { 
                    OrderId = command.OrderId, 
                    Gateway = result.Gateway,
                    Action = result.Action,
                    Url = result.Url,
                    Fields = result.Fields
                });
                
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
            return false; // Retry
        }
    }
}
