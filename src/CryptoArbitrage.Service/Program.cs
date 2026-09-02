using CryptoArbitrage.Service.Configuration;
using CryptoArbitrage.Domain;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services
    .AddOptions<MarketDataOptions>()
    .Bind(builder.Configuration.GetSection(MarketDataOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MarketDataOptions>, MarketDataOptionsValidator>();
builder.Services.AddSingleton<InstrumentRegistry>(serviceProvider =>
    InstrumentRegistryFactory.Create(serviceProvider.GetRequiredService<IOptions<MarketDataOptions>>().Value));

var app = builder.Build();
_ = app.Services.GetRequiredService<InstrumentRegistry>();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (IOptions<MarketDataOptions> options) => Results.Ok(new
{
    status = "ready",
    market = options.Value.Markets.Single().CanonicalSymbol,
    executionEnabled = false
}));

app.Run();

public partial class Program;
