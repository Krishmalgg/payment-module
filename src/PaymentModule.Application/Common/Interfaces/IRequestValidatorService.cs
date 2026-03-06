namespace PaymentModule.Application.Common.Interfaces;

public sealed record IdempotencyCheckResult(bool IsDuplicate, string? CachedResponse);

public interface IRequestValidatorService
{
    /// <summary>
    /// Validates that the given unix timestamp is within the allowed window.
    /// Pure math — no DB access.
    /// </summary>
    bool ValidateTimestamp(long unixTimestamp, int windowSeconds = 30);

    /// <summary>
    /// Checks whether the idempotency key already exists in the store.
    /// Returns IsDuplicate=true + CachedResponse if found and not expired.
    /// </summary>
    Task<IdempotencyCheckResult> CheckIdempotencyAsync(
        string key,
        string requestHash,
        CancellationToken ct = default);

    /// <summary>
    /// Stores an idempotency record after a successful request execution.
    /// </summary>
    Task StoreIdempotencyAsync(
        string key,
        string requestHash,
        string response,
        string source,
        string requestType,
        CancellationToken ct = default);
}
