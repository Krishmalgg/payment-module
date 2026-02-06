namespace PaymentModule.Domain.Ports;

/// <summary>
/// Result returned when initiating a card preapproval (Add Card) process.
/// </summary>
public record PreapprovalResult(
    string Gateway,
    string Action,
    string Url,
    Dictionary<string, string> Fields
);
