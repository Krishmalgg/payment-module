using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using RabbitMQ.Client;
using PaymentModule.Application.Features.Payments.Queries.GetStoredCards;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;
using PaymentModule.Infrastructure.Communication.Core.Connection;
using PaymentModule.Infrastructure.Communication.Core.Producers;

namespace PaymentModule.Infrastructure.Communication.Features.GetStoredCards;

/// <summary>
/// Consumes GetStoredCards requests from RabbitMQ and dispatches them to the application layer.
/// </summary>
public class GetStoredCardsConsumer : RabbitMqBaseConsumer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetStoredCardsConsumer> _logger;

    protected override string QueueName => _configuration["Messaging:RabbitMq:Queues:GetStoredCards"] ?? "payment.storedcards.requests";

    public GetStoredCardsConsumer(
        RabbitMqConnection connection,
        ILogger<GetStoredCardsConsumer> logger,
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
            _logger.LogInformation("Processing GetStoredCards request from RabbitMQ");

            // Standard way: Deserialize RAW request directly
            var request = JsonSerializer.Deserialize<GetStoredCardsQuery>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || request.UserId == Guid.Empty)
            {
                _logger.LogWarning("Received null or invalid GetStoredCards request");
                return true; 
            }

            using var scope = _serviceProvider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Trigger the application logic (Decryption happens here via EF)
            var cards = await mediator.Send(request);

            // Determine response queue priority: ReplyTo Header -> Config Default
            var responseQueue = properties.ReplyTo 
                ?? _configuration["Messaging:RabbitMq:Queues:GetStoredCardsResponse"];

            if (!string.IsNullOrEmpty(responseQueue))
            {
                var producer = scope.ServiceProvider.GetRequiredService<RabbitMqProducer>();
                var responsePayload = JsonSerializer.Serialize(new
                {
                    isSuccess = true,
                    payload = new
                    {
                        cards = cards
                    }
                });

                // Use CorrelationId from request to match response
                await producer.SendAsync(responseQueue, responsePayload, CancellationToken.None, properties.CorrelationId);
                _logger.LogInformation("Successfully sent card details to response queue: {Queue} (CorrelationId: {CorrelationId})",
                    responseQueue, properties.CorrelationId);
            }
            else 
            {
                _logger.LogWarning("GetStoredCards request received without ReplyTo header and no default configured. Cards fetched for {UserId} but not returned.", request.UserId);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process GetStoredCards request from RabbitMQ");
            return false; // Retry
        }
    }
}
