using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;
using PaymentModule.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace PaymentModule.Infrastructure.Communication.Core.Producers;

/// <summary>
/// Generic Producer for sending HTTP requests with S2S Security Headers.
/// </summary>
public class HttpProducer : IMessageProducer
{
    private readonly HttpClient _httpClient;
    private readonly IS2SHeaderGenerator _headerGenerator;
    private readonly ILogger<HttpProducer> _logger;
    private readonly string _targetUrl;

    public HttpProducer(
        HttpClient httpClient, 
        IS2SHeaderGenerator headerGenerator,
        IConfiguration config, // Or specific Options pattern
        ILogger<HttpProducer> logger)
    {
        _httpClient = httpClient;
        _headerGenerator = headerGenerator;
        _logger = logger;
        
        // In a real generic producer, the URL might come from the message metadata or a factory.
        // For this specific implementation, we'll read from config or assume it's passed in 'queue' arg?
        // Let's assume the 'queue' argument in SendAsync contains the Target URL for HTTP strategy.
        // OR we read a default from config. 
        // Let's rely on config for the main notification URL for now.
        _targetUrl = config["PaperMaker:NotificationUrl"] ?? "http://localhost:5201/api/webhooks/notifications/status";
    }

    public async Task<ProducerResult> SendAsync(string endpointOrQueue, string payload, CancellationToken ct, string? correlationId = null)
    {
        try
        {
            // If 'endpointOrQueue' is a valid URL, use it. Otherwise use default from config.
            var url = Uri.IsWellFormedUriString(endpointOrQueue, UriKind.Absolute) 
                ? endpointOrQueue 
                : _targetUrl;

            Console.WriteLine($"\n[HttpProducer] --- NEW S2S REQUEST ---");
            Console.WriteLine($"[HttpProducer] Target: {url}");
            Console.WriteLine($"[HttpProducer] Payload: {payload}");

            // 1. Generate Headers
            var headers = _headerGenerator.GenerateHeaders("POST", url, payload);
            
            Console.WriteLine("[HttpProducer] Outgoing Headers:");
            foreach (var h in headers)
            {
                Console.WriteLine($"  {h.Key}: {h.Value}");
            }

            // 2. Prepare Request
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // 3. Send
            var response = await _httpClient.SendAsync(request, ct);

            // 4. Validate
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("HTTP S2S Request Success: {StatusCode}", response.StatusCode);
                return ProducerResult.Ok();
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                var errorMsg = $"HTTP {response.StatusCode}: {responseBody}";
                _logger.LogWarning("HTTP S2S Request Failed. {Error}", errorMsg);
                return ProducerResult.Fail(errorMsg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP S2S Request Exception");
            return ProducerResult.Fail(ex.Message);
        }
    }
}
