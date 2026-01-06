using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PaymentModule.Domain.Entities;
using PaymentModule.Infrastructure.Persistence.DbContext;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentModule.Api.Middleware;

public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private static readonly TimeSpan DefaultTTL = TimeSpan.FromHours(24);

    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, SecureDbContext dbContext)
    {
        // Only apply to POST/PUT/PATCH methods
        if (!ShouldProcessRequest(context.Request))
        {
            await _next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers[IdempotencyKeyHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(idempotencyKey))
        {
            Console.WriteLine($"[Idempotency] Request received WITHOUT {IdempotencyKeyHeader} header.");
            // No idempotency key, proceed normally
            await _next(context);
            return;
        }

        Console.WriteLine($"[Idempotency] Request received with {IdempotencyKeyHeader}: {idempotencyKey}");

        // Read request body
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        // Compute hash of request
        var requestHash = ComputeHash(requestBody);

        // Check if we've seen this idempotency key before
        var existingRecord = await dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == idempotencyKey);

        if (existingRecord != null)
        {
            if (existingRecord.IsExpired())
            {
                // Expired, remove and process as new
                dbContext.IdempotencyRecords.Remove(existingRecord);
                await dbContext.SaveChangesAsync();
            }
            else if (existingRecord.IsValidFor(requestHash))
            {
                // Same request - return cached response
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(existingRecord.Response);
                return;
            }
            else
            {
                // Different request body with same key
                context.Response.StatusCode = 422;
                await context.Response.WriteAsync("{\"error\":\"Idempotency key already used for different request\"}");
                return;
            }
        }

        // Process request and capture response
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        // Only cache successful responses (2xx)
        if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            var response = await new StreamReader(responseBody).ReadToEndAsync();

            // Store idempotency record
            var record = new IdempotencyRecord(idempotencyKey, requestHash, response, DefaultTTL);
            dbContext.IdempotencyRecords.Add(record);
            await dbContext.SaveChangesAsync();

            responseBody.Seek(0, SeekOrigin.Begin);
        }

        await responseBody.CopyToAsync(originalBodyStream);
    }

    private static bool ShouldProcessRequest(HttpRequest request)
    {
        var method = request.Method.ToUpperInvariant();
        return method == "POST" || method == "PUT" || method == "PATCH";
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}
