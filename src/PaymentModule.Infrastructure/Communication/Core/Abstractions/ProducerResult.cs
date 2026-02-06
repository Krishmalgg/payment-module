namespace PaymentModule.Infrastructure.Communication.Core.Abstractions;

public record ProducerResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static ProducerResult Ok() => new() { Success = true };
    public static ProducerResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}
