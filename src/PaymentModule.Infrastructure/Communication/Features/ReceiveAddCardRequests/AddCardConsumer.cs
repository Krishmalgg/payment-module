using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using RabbitMQ.Client;
using PaymentModule.Application.Features.Payments.Commands.AddCard;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;
using PaymentModule.Infrastructure.Communication.Core.Connection;

namespace PaymentModule.Infrastructure.Communication.Features.ReceiveAddCardRequests;

/// <summary>
/// Consumes AddCard requests from RabbitMQ and dispatches them to the application layer.
/// </summary>
public class AddCardConsumer : RabbitMqBaseConsumer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AddCardConsumer> _logger;

    protected override string QueueName => _configuration["Messaging:RabbitMq:Queues:AddCard"] ?? "temp";

    public AddCardConsumer(
        RabbitMqConnection connection,
        ILogger<AddCardConsumer> logger,
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

            _logger.LogInformation("Successfully initiated preapproval for User {UserId}. OrderId: {OrderId}", command.UserId, result.OrderId);
            
            // Note: Since this is an async consumer, the caller (the one who put the message in RMQ) 
            // won't see the response (result). This is fine for fire-and-forget ingestion.
            // If they need the result, they should use a Callback queue or different pattern.
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process AddCard request from RabbitMQ");
            return false; // Retry
        }
    }
}
