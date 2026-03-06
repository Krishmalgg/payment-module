namespace PaymentModule.Domain.Entities;

public class IdempotencyRecord
{
    public string Key { get; private set; } = default!;
    public string RequestHash { get; private set; } = default!;
    public string Response { get; private set; } = default!;
    public string Source { get; private set; } = default!;       // "Http" | "RabbitMq"
    public string RequestType { get; private set; } = default!;  // "PaymentIntent" | "Refund" | "AddCard"
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public IdempotencyRecord(
        string key,
        string requestHash,
        string response,
        string source,
        string requestType,
        TimeSpan ttl)
    {
        Key = key;
        RequestHash = requestHash;
        Response = response;
        Source = source;
        RequestType = requestType;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = DateTime.UtcNow.Add(ttl);
    }

    public bool IsExpired() => DateTime.UtcNow > ExpiresAt;

    public bool IsValidFor(string requestHash) => RequestHash == requestHash && !IsExpired();

    private IdempotencyRecord() { }
}
