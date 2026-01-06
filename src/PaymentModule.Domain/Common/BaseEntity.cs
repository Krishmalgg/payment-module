namespace PaymentModule.Domain.Common;

public abstract class BaseEntity
{
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    public void MarkAsUpdated()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
