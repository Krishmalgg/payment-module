using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Infrastructure.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentModule.Infrastructure.BackgroundServices;

public class OutboxBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxBackgroundService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PaperMakerOptions _paperMakerOptions;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);

    public OutboxBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<OutboxBackgroundService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<PaperMakerOptions> paperMakerOptions)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _paperMakerOptions = paperMakerOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Background Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("Outbox Background Service stopped");
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var outboxService = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        var messageIds = await outboxService.GetUnprocessedMessageIdsAsync(10, cancellationToken);

        foreach (var messageId in messageIds)
        {
            try
            {
                var message = await outboxService.GetMessageAsync(messageId, cancellationToken);
                if (message == null) continue;

                if (message.Type == "SuspiciousActivity")
                {
                    await NotifySuspiciousActivityAsync(message.Payload, cancellationToken);
                }

                await outboxService.ProcessMessageAsync(messageId, cancellationToken);
                _logger.LogInformation("Processed outbox message {MessageId} (Type: {Type})", messageId, message.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {MessageId}", messageId);
                await outboxService.MarkAsFailedAsync(messageId, ex.Message, cancellationToken);
            }
        }
    }

    private async Task NotifySuspiciousActivityAsync(string payload, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_paperMakerOptions.NotificationUrl))
        {
            _logger.LogWarning("PaperMaker NotificationUrl is not configured. Skipping notification.");
            return;
        }

        var uri = new Uri(_paperMakerOptions.NotificationUrl);
        
        // --- S2S Signing ---
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString();
        var method = "POST";
        // Important: Signature path must include query string if any
        var pathAndQuery = uri.PathAndQuery;
        
        var apiKey = _paperMakerOptions.ApiKeys.FirstOrDefault();
        var hmacSecret = _paperMakerOptions.HmacSecrets.FirstOrDefault();

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(hmacSecret))
        {
             _logger.LogWarning("S2S ApiKey or HmacSecret is missing. Cannot send notification.");
             return;
        }

        // Canonical String: Method + Path + Timestamp + Nonce + Body
        var signaturePayload = $"{method}{pathAndQuery}{timestamp}{nonce}{payload}";
        
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signaturePayload));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();
        // -------------------

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("x-timestamp", timestamp);
        client.DefaultRequestHeaders.Add("x-nonce", nonce);
        client.DefaultRequestHeaders.Add("x-signature", signature);

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        
        var response = await client.PostAsync(_paperMakerOptions.NotificationUrl, content, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"PaperMaker server responded with {response.StatusCode}: {error}");
        }

        _logger.LogInformation("Successfully notified PaperMaker about suspicious activity.");
    }
}
