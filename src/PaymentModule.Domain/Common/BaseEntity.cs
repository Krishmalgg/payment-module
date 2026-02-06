namespace PaymentModule.Domain.Common;

public abstract class BaseEntity
{
    private readonly List<DomainEvent> _domainEvents = new();
    
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(DomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
    
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    public void MarkAsUpdated()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
