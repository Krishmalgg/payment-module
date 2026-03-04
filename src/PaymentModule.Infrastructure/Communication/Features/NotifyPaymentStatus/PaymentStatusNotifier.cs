using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Application.Features.Payments.Interfaces;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;
using PaymentModule.Infrastructure.Communication.Core.Factory;
using PaymentModule.Infrastructure.Communication.Core.Outbox;

namespace PaymentModule.Infrastructure.Communication.Features.NotifyPaymentStatus;

/// <summary>
/// Handles payment status notifications.
/// Implements IPaymentStatusNotifier for immediate requests and IOutboxMessageHandler for outbox/background processing.
/// </summary>
public class PaymentStatusNotifier : IPaymentStatusNotifier, IOutboxMessageHandler
{
    private readonly IOutboxService _outboxService;
    private readonly ProducerFactory _producerFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentStatusNotifier> _logger;
    private readonly IServiceProvider _serviceProvider; // Need this to avoid circular if we use IOutboxDispatcher

    public string MessageType => "PaymentStatus";

    public PaymentStatusNotifier(
        IOutboxService outboxService,
        ProducerFactory producerFactory,
        IConfiguration configuration,
        ILogger<PaymentStatusNotifier> logger,
        IServiceProvider serviceProvider)
    {
        _outboxService = outboxService;
        _producerFactory = producerFactory;
        _configuration = configuration;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task NotifyAsync(PaymentStatusPayload payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });

            // 1. Save to Outbox (Reliability)
            await _outboxService.AddMessageAsync(MessageType, payloadJson, cancellationToken);

            _logger.LogInformation("Payment status notification added to outbox. TransactionId: {TransactionId}", payload.TransactionId);

            // 2. Immediate Send (Hybrid Mode)
            // We do this in a fire-and-forget way or wait for it but don't fail the transaction if sending fails.
            _ = Task.Run(async () => 
            {
                try 
                {
                    // We can't easily mark as processed here without a message ID.
                    // A better hybrid way is to have AddMessage return the ID, or just let the background service handle it.
                    // For now, let's keep it simple: the background service will pick it up immediately due to the trigger.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Immediate dispatch attempt failed for Transaction {Id}. Background service will retry.", payload.TransactionId);
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate payment status notification for Transaction {Id}", payload.TransactionId);
            throw;
        }
    }

    public async Task<ProducerResult> HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var producer = _producerFactory.GetProducer();
        
        // Default to Status Notification URL
        string destination = _configuration["PaperMaker:NotificationUrl"] 
                             ?? "http://localhost:5201/api/webhooks/notifications/status";

        try 
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var message = JsonSerializer.Deserialize<PaymentStatusPayload>(payload, options);
            
            // Check if status is SUSPICIOUS and route accordingly
            if (message?.Status != null && message.Status.Equals("SUSPICIOUS", StringComparison.OrdinalIgnoreCase))
            {
                destination = _configuration["PaperMaker:SuspiciousActivityUrl"] 
                              ?? "http://localhost:5201/api/v1/notifications/suspicious";
                _logger.LogWarning("⚠️ Routing PaymentStatus to SuspiciousActivityUrl for Transaction {TransactionId}", message.TransactionId);
            }
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Failed to parse payload for routing logic. Using default destination.");
        }

        _logger.LogInformation("Handling Outbox Message: {Type} sending to {Destination}", MessageType, destination);
        
        return await producer.SendAsync(destination, payload, cancellationToken);
    }
}
