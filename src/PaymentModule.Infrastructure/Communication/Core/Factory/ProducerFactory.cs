using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection; // For IServiceProvider
using Microsoft.Extensions.Logging;
using PaymentModule.Infrastructure.Communication.Core.Abstractions;
using PaymentModule.Infrastructure.Communication.Core.Producers;

namespace PaymentModule.Infrastructure.Communication.Core.Factory;

public class ProducerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProducerFactory> _logger;

    public ProducerFactory(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<ProducerFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public IMessageProducer GetProducer()
    {
        var provider = _configuration["PaymentServer:CommunicationMode"] ?? _configuration["Messaging:Provider"] ?? "RabbitMq";
        
        // Case-insensitive check
        if (string.Equals(provider, "Http", StringComparison.OrdinalIgnoreCase))
        {
            // _logger.LogInformation("Using Messaging Strategy: HTTP"); // Verbose
            return _serviceProvider.GetRequiredService<HttpProducer>();
        }
        
        // Default to RabbitMQ
        // _logger.LogInformation("Using Messaging Strategy: RabbitMQ"); // Verbose
        return _serviceProvider.GetRequiredService<RabbitMqProducer>();
    }
}
