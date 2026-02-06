using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace PaymentModule.Infrastructure.Communication.Core.Connection;

/// <summary>
/// Manages a persistent TCP connection to RabbitMQ.
/// Connections are long-lived and heavy; Channels are lightweight.
/// </summary>
public class RabbitMqConnection : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqConnection> _logger;
    private IConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public RabbitMqConnection(IConfiguration configuration, ILogger<RabbitMqConnection> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_connection != null && _connection.IsOpen)
        {
            return _connection;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection != null && _connection.IsOpen)
            {
                return _connection;
            }

            _logger.LogInformation("Creating new RabbitMQ connection...");

            // In v7.0 we configure the factory and await CreateConnectionAsync
            var factory = new ConnectionFactory
            {
                HostName = _configuration["Messaging:RabbitMq:Host"] ?? "localhost",
                UserName = _configuration["Messaging:RabbitMq:Username"] ?? "guest",
                Password = _configuration["Messaging:RabbitMq:Password"] ?? "guest",
                Port = int.TryParse(_configuration["Messaging:RabbitMq:Port"], out var p) ? p : 5672,

            };

            _connection = await factory.CreateConnectionAsync(ct);
            _logger.LogInformation("RabbitMQ connection established.");

            return _connection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        // In v7.0 connection is IAsyncDisposable, but we implement basic Dispose pattern for DI
        // Ideally we should call DisposeAsync()
        _connection?.Dispose();
        _lock.Dispose();
        _disposed = true;
    }
}
