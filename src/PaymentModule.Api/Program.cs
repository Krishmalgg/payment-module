var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.Configure<PaymentModule.Infrastructure.Gateways.PayHereOptions>(builder.Configuration.GetSection("PayHere"));
builder.Services.AddSingleton<PaymentModule.Domain.Ports.ICryptoProvider, PaymentModule.Infrastructure.Security.AesCryptoProvider>();

var provider = builder.Configuration["PaymentGateway:Provider"] ?? "Mock";
if (string.Equals(provider, "PayHere", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<PaymentModule.Domain.Ports.IPaymentGateway, PaymentModule.Infrastructure.Gateways.Adapters.PayHereAdapter>();
}
else if (string.Equals(provider, "Stripe", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<PaymentModule.Domain.Ports.IPaymentGateway, PaymentModule.Infrastructure.Gateways.Adapters.StripeAdapter>();
}
else
{
    builder.Services.AddSingleton<PaymentModule.Domain.Ports.IPaymentGateway, PaymentModule.Infrastructure.Gateways.Adapters.MockAdapter>();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseMiddleware<PaymentModule.Api.Middleware.SecureEnvelopeMiddleware>();
app.MapControllers();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
