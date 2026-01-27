using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Infrastructure.Communication.Core.Outbox;

namespace PaymentModule.Infrastructure.BackgroundServices;

public class OutboxBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IOutboxTrigger _trigger;
    private readonly TimeSpan _defaultInterval = TimeSpan.FromSeconds(30);

    public OutboxBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<OutboxBackgroundService> logger,
        IConfiguration configuration,
        IOutboxTrigger trigger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _trigger = trigger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Background Service started (Dispatcher mode)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool messagesProcessed;
                do
                {
                    messagesProcessed = await ProcessOutboxMessagesAsync(stoppingToken);
                } while (messagesProcessed && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(_defaultInterval);

            try 
            {
                await _trigger.WaitForTriggerAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        }

        _logger.LogInformation("Outbox Background Service stopped");
    }

    private async Task<bool> ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

        var messageIds = await outboxService.GetUnprocessedMessageIdsAsync(10, cancellationToken);

        if (!messageIds.Any()) return false;

        foreach (var messageId in messageIds)
        {
            try
            {
                var message = await outboxService.GetMessageAsync(messageId, cancellationToken);
                if (message == null) continue;

                _logger.LogInformation("Processing Outbox Message: {Id} Type: {Type}", messageId, message.Type);

                // Dispatch via feature-specific handler
                var result = await dispatcher.DispatchAsync(message, cancellationToken);

                if (result.Success)
                {
                    await outboxService.ProcessMessageAsync(messageId, cancellationToken);
                    _logger.LogInformation("Successfully processed outbox message {MessageId}", messageId);
                }
                else
                {
                    _logger.LogWarning("Failed to process message {MessageId}. Error: {Error}. Retrying in 10s...", messageId, result.ErrorMessage);
                    await Task.Delay(10000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while processing outbox message {MessageId}. Retrying in 10s...", messageId);
                await Task.Delay(10000, cancellationToken);
            }
        }
        
        return true;
    }
}
