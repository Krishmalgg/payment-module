using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Infrastructure.Communication.Core.Outbox;
using PaymentModule.Infrastructure.Configuration;

namespace PaymentModule.Infrastructure.BackgroundServices;

public class OutboxBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxBackgroundService> _logger;
    private readonly IOutboxTrigger _trigger;
    private readonly OutboxProcessingOptions _options;
    private readonly TimeSpan _defaultInterval;

    public OutboxBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<OutboxBackgroundService> logger,
        IOptions<OutboxProcessingOptions> optionsAccessor,
        IOutboxTrigger trigger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _trigger = trigger;
        _options = optionsAccessor.Value;
        _defaultInterval = TimeSpan.FromSeconds(_options.ProcessingIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Outbox Background Service is disabled");
            return;
        }

        var scaleLevel = DetermineScaleLevel();
        _logger.LogInformation("Outbox Background Service started (Scale Level: {ScaleLevel})", scaleLevel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool messagesProcessed;
                do
                {
                    messagesProcessed = await ProcessOutboxMessagesAsync(scaleLevel, stoppingToken);
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

    private int DetermineScaleLevel()
    {
        if (_options.AutoScale)
        {
            // Auto-scaling logic based on message volume
            // This is a placeholder - implement based on your requirements
            // For example: check outbox message count and scale accordingly
            return 2;  // Default to Level 2 for now
        }

        return _options.ScaleLevel;
    }

    private async Task<bool> ProcessOutboxMessagesAsync(int scaleLevel, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

        // Get unprocessed message IDs — excludes messages that exceeded max retry attempts
        var messageIds = await outboxService.GetUnprocessedMessageIdsAsync(_options.BatchSize, _options.MaxRetryAttempts, cancellationToken);

        if (!messageIds.Any()) return false;

        // Select strategy based on scale level
        var strategy = GetStrategy(scaleLevel, scope);

        _logger.LogDebug("Processing {Count} messages with strategy Level {ScaleLevel}", messageIds.Count(), scaleLevel);

        try
        {
            var processedCount = await strategy.ProcessMessagesAsync(messageIds, outboxService, dispatcher, cancellationToken);
            _logger.LogInformation("Processed {ProcessedCount} of {TotalCount} outbox messages", processedCount, messageIds.Count());
            return processedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during outbox message processing");
            return false;
        }
    }

    private IOutboxProcessingStrategy GetStrategy(int scaleLevel, IServiceScope scope)
    {
        return scaleLevel switch
        {
            1 => scope.ServiceProvider.GetRequiredService<SequentialOutboxProcessingStrategy>(),
            2 => scope.ServiceProvider.GetRequiredService<ParallelOutboxProcessingStrategy>(),
            3 => scope.ServiceProvider.GetRequiredService<DistributedMultithreadedOutboxProcessingStrategy>(),
            _ => scope.ServiceProvider.GetRequiredService<SequentialOutboxProcessingStrategy>()
        };
    }
}
