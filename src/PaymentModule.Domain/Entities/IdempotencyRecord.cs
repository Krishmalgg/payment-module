namespace PaymentModule.Domain.Entities;

public class IdempotencyRecord
{
    public string Key { get; private set; } = default!;
    public string RequestHash { get; private set; } = default!;
    public string Response { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    public IdempotencyRecord(string key, string requestHash, string response, TimeSpan ttl)
    {
        Key = key;
        RequestHash = requestHash;
        Response = response;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = DateTime.UtcNow.Add(ttl);
    }

    public bool IsExpired() => DateTime.UtcNow > ExpiresAt;

    public bool IsValidFor(string requestHash) => RequestHash == requestHash && !IsExpired();

    private IdempotencyRecord() { }
}
