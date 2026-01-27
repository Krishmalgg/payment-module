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
builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<SecureDbContext>());

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

// Load .env file
DotNetEnv.Env.Load();

// Bind S2S Options
builder.Services.Configure<S2SSecurityOptions>(options =>
{
    // 1. Load from appsettings/config first
    var section = builder.Configuration.GetSection("Security");
    options.ApiKeys = section.GetSection("ApiKeys").Get<string[]>() ?? [];
    options.HmacSecrets = section.GetSection("HmacSecrets").Get<string[]>() ?? [];

    // 2. Override with .env if keys exist
    var newKey = DotNetEnv.Env.GetString("NEW_S2S_API_KEY");
    var oldKey = DotNetEnv.Env.GetString("OLD_S2S_API_KEY");
    var newSecret = DotNetEnv.Env.GetString("NEW_S2S_HMAC_SECRET");
    var oldSecret = DotNetEnv.Env.GetString("OLD_S2S_HMAC_SECRET");

    var envApiKeys = new List<string>();
    var envSecrets = new List<string>();

    if (!string.IsNullOrWhiteSpace(newKey)) envApiKeys.Add(newKey);
    if (!string.IsNullOrWhiteSpace(oldKey)) envApiKeys.Add(oldKey);

    if (!string.IsNullOrWhiteSpace(newSecret)) envSecrets.Add(newSecret);
    if (!string.IsNullOrWhiteSpace(oldSecret)) envSecrets.Add(oldSecret);
    
    if (envApiKeys.Count > 0) options.ApiKeys = envApiKeys.ToArray();
    if (envSecrets.Count > 0) options.HmacSecrets = envSecrets.ToArray();
});

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
// Resilience Configuration
builder.Services.AddResiliencePipeline("payment-gateway", builder =>
{
    builder.AddPipeline(PaymentModule.Infrastructure.Configuration.ResiliencePolicies.CreatePaymentGatewayPipeline());
});

// PaperMaker Configuration
builder.Services.Configure<PaymentModule.Infrastructure.Configuration.PaperMakerOptions>(options =>
{
    builder.Configuration.GetSection("PaperMaker").Bind(options);
    
    var newKey = DotNetEnv.Env.GetString("NEW_S2S_API_KEY");
    var oldKey = DotNetEnv.Env.GetString("OLD_S2S_API_KEY");
    var newSecret = DotNetEnv.Env.GetString("NEW_S2S_HMAC_SECRET");
    var oldSecret = DotNetEnv.Env.GetString("OLD_S2S_HMAC_SECRET");

    options.ApiKeys = new List<string>();
    options.HmacSecrets = new List<string>();

    if (!string.IsNullOrWhiteSpace(newKey)) options.ApiKeys.Add(newKey);
    if (!string.IsNullOrWhiteSpace(oldKey)) options.ApiKeys.Add(oldKey);

    if (!string.IsNullOrWhiteSpace(newSecret)) options.HmacSecrets.Add(newSecret);
    if (!string.IsNullOrWhiteSpace(oldSecret)) options.HmacSecrets.Add(oldSecret);
});
builder.Services.AddHttpClient();

var provider = builder.Configuration["PaymentGateway:Provider"] ?? "Mock";
if (string.Equals(provider, "PayHere", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<PaymentModule.Infrastructure.Configuration.PayHereOptions>(
        builder.Configuration.GetSection("PayHere"));
    builder.Services.AddSingleton<IPaymentGateway, PayHereAdapter>();
}
else
{
    // builder.Services.AddSingleton<IPaymentGateway, MockAdapter>();
}

// Infrastructure Services
builder.Services.AddScoped<IOutboxService, OutboxService>();
builder.Services.AddSingleton<IOutboxTrigger, OutboxTrigger>();
builder.Services.AddSingleton<PaymentModule.Infrastructure.Communication.Core.Connection.RabbitMqConnection>();
builder.Services.AddTransient<IS2SHeaderGenerator, S2SHeaderGenerator>();

// Feature-Specific Notifiers
builder.Services.AddScoped<PaymentModule.Application.Features.Payments.Interfaces.IPaymentStatusNotifier, 
    PaymentModule.Infrastructure.Communication.Features.NotifyPaymentStatus.PaymentStatusNotifier>();
builder.Services.AddScoped<PaymentModule.Infrastructure.Communication.Core.Outbox.IOutboxMessageHandler, 
    PaymentModule.Infrastructure.Communication.Features.NotifyPaymentStatus.PaymentStatusNotifier>();
builder.Services.AddScoped<PaymentModule.Infrastructure.Communication.Core.Outbox.IOutboxDispatcher, 
    PaymentModule.Infrastructure.Communication.Core.Outbox.OutboxDispatcher>();

// Producers (Registered as concrete types or via Factory later)
builder.Services.AddTransient<PaymentModule.Infrastructure.Communication.Core.Producers.RabbitMqProducer>();
builder.Services.AddTransient<PaymentModule.Infrastructure.Communication.Core.Producers.HttpProducer>();
builder.Services.AddSingleton<PaymentModule.Infrastructure.Communication.Core.Factory.ProducerFactory>();

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
app.UseMiddleware<S2SSecurityMiddleware>(); // Added S2S Security
app.UseMiddleware<IdempotencyMiddleware>();

// Routing
app.MapControllers();

// Health Check Endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();
