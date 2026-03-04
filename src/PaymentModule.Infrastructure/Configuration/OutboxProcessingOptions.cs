namespace PaymentModule.Infrastructure.Configuration;

public class OutboxProcessingOptions
{
    public bool Enabled { get; set; } = true;
    public bool AutoScale { get; set; } = false;
    public int ScaleLevel { get; set; } = 1;  // 1 = Sequential, 2 = Parallel, 3 = Distributed Multi-threaded
    public int MaxDegreeOfParallelism { get; set; } = 4;
    public int BatchSize { get; set; } = 10;
    public int ProcessingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Delays between retry attempts in seconds.
    /// Index 0 = delay after 1st failure, index 1 = delay after 2nd failure, etc.
    /// Total attempts = RetryDelaysSeconds.Length + 1.
    /// Default: [10, 60] → 3 attempts total (0s immediate, +10s, +60s) then DLQ.
    /// MaxRetryAttempts must equal RetryDelaysSeconds.Length + 1.
    /// </summary>
    public int[] RetryDelaysSeconds { get; set; } = [10, 60];

    /// <summary>Must equal RetryDelaysSeconds.Length + 1.</summary>
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 5000;
    public int LockTimeoutSeconds { get; set; } = 60;
    public string ProcessInstanceId { get; set; } = "payment-api-default";

    /// <summary>
    /// Transport used by the outbox dispatcher: "RabbitMq" or "Http".
    /// Stored in FailedMessages.CommunicationType when a message exhausts all retries.
    /// Should match PaymentServer:CommunicationMode in appsettings.
    /// </summary>
    public string CommunicationMode { get; set; } = "RabbitMq";
}
