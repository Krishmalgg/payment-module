using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Papermaker.PaymentSDKNEW.Core.Messaging;

namespace PaymentModule.Api.Security;

/// <summary>
/// IAsyncResultFilter — runs after every secured action.
///
/// Responsibilities (single responsibility each, delegated):
///   1. Extract the raw business-logic payload from ObjectResult
///   2. Serialise the PAYLOAD ONLY (this is what gets signed)
///   3. Wrap payload + metadata in a MessageEnvelope (same shape as Main Server SDK)
///   4. Delegate HMAC signing + HTTP header injection to SecurityHeadersHandler
///   5. Replace context.Result with the envelope so ASP.NET writes it
///
/// WHAT IS SIGNED:
///   HMAC is over payload JSON, NOT over the envelope.
///   Main Server verifies: extract envelope.Payload, recompute HMAC with the response headers.
///
/// Only registered when Security:IsNewS2sValidation = true (Program.cs).
/// </summary>
public class S2SResponseSigningFilter : IAsyncResultFilter
{
    private readonly EnvelopeFactory _envelopeFactory;
    private readonly SecurityHeadersHandler _headersHandler;
    private readonly ILogger<S2SResponseSigningFilter> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public S2SResponseSigningFilter(
        EnvelopeFactory envelopeFactory,
        SecurityHeadersHandler headersHandler,
        ILogger<S2SResponseSigningFilter> logger)
    {
        _envelopeFactory = envelopeFactory;
        _headersHandler  = headersHandler;
        _logger          = logger;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var request  = context.HttpContext.Request;
        var response = context.HttpContext.Response;

        // ── 1. Extract raw payload ────────────────────────────────────────────
        var rawPayload = (context.Result as ObjectResult)?.Value;

        // ── 2. Serialise PAYLOAD ONLY — this is the body that will be signed ──
        var payloadJson = rawPayload is not null
            ? JsonSerializer.Serialize(rawPayload, JsonOpts)
            : "{}";

        // ── 3. Create envelope (Payload + metadata headers bag) ───────────────
        var correlationId  = request.Headers["X-Correlation-ID"].FirstOrDefault()
                             ?? Guid.NewGuid().ToString();
        var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault();

        // Determine success: 2xx status codes are success
        var isSuccess = context.Result is ObjectResult objResult && objResult.StatusCode.HasValue
            ? objResult.StatusCode.Value is >= 200 and < 300
            : true;

        var envelope = _envelopeFactory.Create(rawPayload, correlationId, idempotencyKey, isSuccess);

        // ── 4. Sign PAYLOAD and inject security headers ───────────────────────
        var method       = request.Method.ToUpperInvariant();
        var pathAndQuery = (request.PathBase + request.Path + request.QueryString).ToString();

        var (nonce, timestamp, signature) =
            _headersHandler.AddResponseHeaders(response.Headers, method, pathAndQuery, payloadJson);

        // Echo correlation / idempotency headers so Main Server can match the response
        response.Headers["X-Correlation-ID"] = correlationId;
        if (!string.IsNullOrEmpty(idempotencyKey))
            response.Headers["Idempotency-Key"] = idempotencyKey;

        Console.WriteLine("[S2SResponseSigningFilter] --- Response Signed ---");
        Console.WriteLine($"[S2SResponseSigningFilter]   IsSuccess       : {isSuccess}");
        Console.WriteLine($"[S2SResponseSigningFilter]   Payload     : {payloadJson}");
        Console.WriteLine($"[S2SResponseSigningFilter]   Timestamp   : {timestamp}");
        Console.WriteLine($"[S2SResponseSigningFilter]   Nonce       : {nonce}");
        Console.WriteLine($"[S2SResponseSigningFilter]   Signature   : {signature}");
        var ikDisplay = idempotencyKey ?? "none";
        Console.WriteLine($"[S2SResponseSigningFilter]   CorrelationId : {correlationId}");
        Console.WriteLine($"[S2SResponseSigningFilter]   IdempotencyKey: {ikDisplay}");
        Console.WriteLine("[S2SResponseSigningFilter] --- Response Headers ---");
        foreach (var h in response.Headers.Where(h => h.Key.StartsWith("x-") || h.Key == "X-Correlation-ID"))
            Console.WriteLine($"[S2SResponseSigningFilter]   {h.Key}: {string.Join(",", h.Value.ToArray())}");

        _logger.LogInformation(
            "[S2SResponseSigningFilter] {Method} {Path} | isSuccess={Success} | correlationId={C} | idempotencyKey={IK}",
            method, pathAndQuery, isSuccess, correlationId, ikDisplay);

        // ── 5. Replace result with the envelope ───────────────────────────────
        context.Result = new OkObjectResult(envelope);

        await next();
    }
}

