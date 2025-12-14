using AspNetCoreRateLimit;
using FluentValidation;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using PaymentModule.Api.HealthChecks;
using PaymentModule.Api.Middleware;
using PaymentModule.Application.Common.Behaviors;
using PaymentModule.Application.Common.Interfaces;
using PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;
using PaymentModule.Domain.Ports;
using PaymentModule.Infrastructure.BackgroundServices;
using PaymentModule.Infrastructure.Gateways.Adapters;
using PaymentModule.Infrastructure.Persistence.DbContext;
using PaymentModule.Infrastructure.Security;
using PaymentModule.Infrastructure.Services;
using Serilog;
using Polly;

var builder = WebApplication.CreateBuilder(args);

// Serilog Configuration
builder.Host.UseSerilog((context, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/payment-module-.log", rollingInterval: RollingInterval.Day);
});

// Database Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<SecureDbContext>(options =>
    options.UseNpgsql(connectionString));

// MediatR Configuration
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssembly(typeof(CreatePaymentIntentCommand).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<CreatePaymentIntentValidator>();

// Rate Limiting Configuration
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.Configure<ClientRateLimitOptions>(builder.Configuration.GetSection("ClientRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, PaymentModule.Api.Configuration.PaymentRateLimitConfiguration>();

// CORS Configuration
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
    ?? new[] { "http://localhost:3000" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowedOrigins", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Payment Gateway Configuration
builder.Services.AddSingleton<ICryptoProvider, AesCryptoProvider>();
// Resilience Configuration
builder.Services.AddResiliencePipeline("payment-gateway", builder =>
{
    builder.AddPipeline(PaymentModule.Infrastructure.Configuration.ResiliencePolicies.CreatePaymentGatewayPipeline());
});

var provider = builder.Configuration["PaymentGateway:Provider"] ?? "Mock";
if (string.Equals(provider, "PayHere", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<PaymentModule.Infrastructure.Configuration.PayHereOptions>(
        builder.Configuration.GetSection("PayHere"));
    builder.Services.AddSingleton<IPaymentGateway, PayHereAdapter>();
}

else if (string.Equals(provider, "Stripe", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IPaymentGateway, StripeAdapter>();
}
else
{
    builder.Services.AddSingleton<IPaymentGateway, MockAdapter>();
}

// Infrastructure Services
builder.Services.AddScoped<IOutboxService, OutboxService>();

// Background Services
builder.Services.AddHostedService<OutboxBackgroundService>();

// Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString!)
    .AddCheck<PaymentGatewayHealthCheck>("payment_gateway");

// API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1);
    options.ReportApiVersions = true;
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = Asp.Versioning.ApiVersionReader.Combine(
        new Asp.Versioning.UrlSegmentApiVersionReader(),
        new Asp.Versioning.HeaderApiVersionReader("X-Api-Version")
    );
})
.AddMvc()
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'V";
    options.SubstituteApiVersionInUrl = true;
});

// Controllers and OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Middleware Pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Serilog Request Logging
app.UseSerilogRequestLogging();

// CORS
app.UseCors("AllowedOrigins");

// Rate Limiting
app.UseIpRateLimiting();

// Custom Middleware
app.UseMiddleware<IdempotencyMiddleware>();
app.UseMiddleware<SecureEnvelopeMiddleware>();

// Routing
app.MapControllers();

// Health Check Endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();
