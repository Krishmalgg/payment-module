using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PaymentModule.Application.DTOs;
using PaymentModule.Domain.Ports;

namespace PaymentModule.Api.Middleware
{
    public class SecureEnvelopeMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ICryptoProvider _crypto;
        private readonly ILogger<SecureEnvelopeMiddleware> _logger;

        public SecureEnvelopeMiddleware(RequestDelegate next, ICryptoProvider crypto, ILogger<SecureEnvelopeMiddleware> logger)
        {
            _next = next;
            _crypto = crypto;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Skip webhooks or swagger
            if (context.Request.Path.StartsWithSegments("/api/v1/webhooks") || 
                context.Request.Path.StartsWithSegments("/swagger") ||
                context.Request.Path.StartsWithSegments("/scalar"))
            {
                await _next(context);
                return;
            }

            // Check if client wants secure exchange
            // For now, we assume all API calls to payments should be secured if possible, 
            // but for backward compatibility/testing, we might check a header or just try.
            // Let's rely on a header "X-Encrypted: true" for requests.
            
            bool isEncryptedRequest = context.Request.Headers.ContainsKey("X-Encrypted");

            if (isEncryptedRequest)
            {
                try
                {
                    await DecryptRequestAsync(context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to decrypt request");
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid encrypted payload");
                    return;
                }
            }

            // Capture response to encrypt it
            // We only encrypt if the client requested it OR if we force it. 
            // Let's say we encrypt if the request was encrypted OR if a header "X-Response-Encrypt: true" is set.
            // For this demo, let's encrypt if "X-Encrypted" was sent.
            
            if (isEncryptedRequest)
            {
                var originalBodyStream = context.Response.Body;
                using var newBodyStream = new MemoryStream();
                context.Response.Body = newBodyStream;

                try
                {
                    await _next(context);

                    context.Response.Body = originalBodyStream;
                    newBodyStream.Seek(0, SeekOrigin.Begin);
                    var responseBody = await new StreamReader(newBodyStream).ReadToEndAsync();

                    // Only encrypt 200 OK responses that are JSON
                    if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
                    {
                        var encrypted = _crypto.Encrypt(responseBody);
                        var envelope = new SecureResponseDto { Data = encrypted };
                        var json = JsonSerializer.Serialize(envelope);
                        
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(json);
                    }
                    else
                    {
                        // If error, maybe return as is?
                        await context.Response.WriteAsync(responseBody);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during secure response processing");
                    context.Response.Body = originalBodyStream;
                    throw; 
                }
            }
            else
            {
                await _next(context);
            }
        }

        private async Task DecryptRequestAsync(HttpContext context)
        {
            context.Request.EnableBuffering();

            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            
            if (string.IsNullOrWhiteSpace(body)) return;

            var envelope = JsonSerializer.Deserialize<SecureResponseDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.Data))
            {
                throw new InvalidOperationException("Empty encrypted data");
            }

            var decryptedJson = _crypto.Decrypt(envelope.Data);
            
            // Replace request body with decrypted stream
            var bytes = System.Text.Encoding.UTF8.GetBytes(decryptedJson);
            var stream = new MemoryStream(bytes);
            context.Request.Body = stream;
            context.Request.ContentLength = stream.Length;
        }
    }
}