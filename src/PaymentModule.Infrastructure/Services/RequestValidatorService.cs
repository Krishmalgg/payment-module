using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Domain.Entities;
using PaymentModule.Infrastructure.Persistence.DbContext;

namespace PaymentModule.Infrastructure.Services;

public class RequestValidatorService : IRequestValidatorService
{
    private readonly SecureDbContext _dbContext;
    private readonly ILogger<RequestValidatorService> _logger;
    private static readonly TimeSpan DefaultTTL = TimeSpan.FromHours(24);

    public RequestValidatorService(SecureDbContext dbContext, ILogger<RequestValidatorService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // ── Timestamp ─────────────────────────────────────────────────────────────

    public bool ValidateTimestamp(long unixTimestamp, int windowSeconds = 30)
    {
        var diff = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixTimestamp);
        if (diff > windowSeconds)
        {
            _logger.LogWarning("[RequestValidator] Timestamp rejected: diff={Diff}s > {Window}s.", diff, windowSeconds);
            return false;
        }
        return true;
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    public async Task<IdempotencyCheckResult> CheckIdempotencyAsync(
        string key,
        string requestHash,
        CancellationToken ct = default)
    {
        var existing = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key, ct);

        if (existing == null)
            return new IdempotencyCheckResult(false, null);

        // Expired — delete and treat as new
        if (existing.IsExpired())
        {
            _dbContext.IdempotencyRecords.Remove(existing);
            await _dbContext.SaveChangesAsync(ct);
            return new IdempotencyCheckResult(false, null);
        }

        // Same key, different body — duplicate but no valid cached response
        if (!existing.IsValidFor(requestHash))
        {
            _logger.LogWarning("[RequestValidator] Key {Key} reused with different body.", key);
            return new IdempotencyCheckResult(true, null);
        }

        _logger.LogInformation("[RequestValidator] Duplicate detected. Key={Key} Source={Source} Type={Type}",
            key, existing.Source, existing.RequestType);

        return new IdempotencyCheckResult(true, existing.Response);
    }

    public async Task StoreIdempotencyAsync(
        string key,
        string requestHash,
        string response,
        string source,
        string requestType,
        CancellationToken ct = default)
    {
        var record = new IdempotencyRecord(key, requestHash, response, source, requestType, DefaultTTL);
        _dbContext.IdempotencyRecords.Add(record);
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("[RequestValidator] Stored idempotency record. Key={Key} Source={Source} Type={Type}",
            key, source, requestType);
    }
}
