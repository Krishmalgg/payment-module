using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace PaymentModule.Api.Middleware;

public class RequestBodyLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestBodyLoggingMiddleware> _logger;
    private const int MaxLogBytes = 16 * 1024; // 16KB

    public RequestBodyLoggingMiddleware(RequestDelegate next, ILogger<RequestBodyLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only log for requests that have a body
        if (context.Request.ContentLength == null || context.Request.ContentLength == 0)
        {
            await _next(context);
            return;
        }

        try
        {
            context.Request.EnableBuffering();
            context.Request.Body.Position = 0;

            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var fullBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            var preview = fullBody.Length > MaxLogBytes ? fullBody.Substring(0, MaxLogBytes) + "...(truncated)" : fullBody;

            if (!string.IsNullOrEmpty(context.Request.ContentType) &&
                context.Request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var doc = JsonDocument.Parse(preview);
                    var redacted = RedactJson(doc.RootElement);
                    _logger.LogInformation("[Body Logger] {Method} {Path} Payload: {Payload}", context.Request.Method, context.Request.Path, redacted);
                }
                catch (JsonException)
                {
                    _logger.LogInformation("[Body Logger] {Method} {Path} Payload (raw-preview): {Payload}", context.Request.Method, context.Request.Path, preview);
                }
            }
            else
            {
                _logger.LogInformation("[Body Logger] {Method} {Path} Payload (raw-preview): {Payload}", context.Request.Method, context.Request.Path, preview);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Body Logger] Failed to read request body for {Path}", context.Request.Path);
        }

        await _next(context);
    }

    private static string RedactJson(JsonElement element)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);
        WriteElement(writer, element);
        writer.Flush();
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    if (IsSensitiveKey(prop.Name))
                    {
                        writer.WriteStringValue("***REDACTED***");
                    }
                    else
                    {
                        WriteElement(writer, prop.Value);
                    }
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l)) writer.WriteNumberValue(l);
                else if (element.TryGetDouble(out var d)) writer.WriteNumberValue(d);
                else writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteRawValue(element.GetRawText());
                break;
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        var s = key.Trim().ToLowerInvariant();
        var sensitive = new[] { "cardnumber", "card_number", "cvv", "cvv2", "pan", "password", "token", "secret", "apikey", "api_key", "x-api-key", "x-signature", "x-hmac-signature" };
        return sensitive.Contains(s);
    }
}
