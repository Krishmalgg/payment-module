using Microsoft.AspNetCore.Http;

namespace PaymentModule.Api.Middleware;

public class RequestHeaderLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestHeaderLoggingMiddleware> _logger;

    public RequestHeaderLoggingMiddleware(RequestDelegate next, ILogger<RequestHeaderLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        _logger.LogInformation("[Header Logger] Incoming request {Method} {Path}", context.Request.Method, context.Request.Path);
        foreach (var header in context.Request.Headers)
        {
            _logger.LogInformation("[Header Logger] {Header}: {Value}", header.Key, header.Value.ToString());
        }

        await _next(context);
    }
}
