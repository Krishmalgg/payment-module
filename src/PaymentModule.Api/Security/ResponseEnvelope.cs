// Mirrors the MessageEnvelope used in the Main Server SDK (Papermaker.PaymentSDKNEW.Core.Messaging)
// so both sides work with identical envelope shapes.
namespace Papermaker.PaymentSDKNEW.Core.Messaging;

/// <summary>Non-generic base envelope — carries any payload alongside a metadata header bag.</summary>
public class MessageEnvelope
{
    public object Payload { get; set; } = new();

    /// <summary>Quick status indicator — Main Server checks this first without parsing payload.</summary>
    public bool IsSuccess { get; set; } = true;
}

/// <summary>Strongly-typed envelope — Payload is cast to T transparently.</summary>
public class MessageEnvelope<T> : MessageEnvelope
{
    public new T Payload
    {
        get => (T)base.Payload;
        set => base.Payload = value!;
    }
}
