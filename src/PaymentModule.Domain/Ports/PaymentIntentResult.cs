namespace PaymentModule.Domain.Ports;

public record PaymentIntentResult(
    string Gateway,
    string Action,
    string Url,
    Dictionary<string, string> Fields
);
