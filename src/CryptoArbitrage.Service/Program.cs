using CryptoArbitrage.Service.Configuration;
using CryptoArbitrage.Service.Opportunities;
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
builder.Services.AddSingleton<ISpreadOpportunityStore, InMemorySpreadOpportunityStore>();

var app = builder.Build();
_ = app.Services.GetRequiredService<InstrumentRegistry>();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (IOptions<MarketDataOptions> options) => Results.Ok(new
{
    status = "ready",
    market = options.Value.Markets.Single().CanonicalSymbol,
    executionEnabled = false
}));

app.MapPost("/api/opportunities/evaluate", (
    SpreadEvaluationRequest? request,
    IOptions<MarketDataOptions> options,
    ISpreadOpportunityStore store) =>
{
    if (request is null)
    {
        return Results.BadRequest(new { error = "A top-of-book payload is required." });
    }

    var market = options.Value.Markets.Single();
    var instrument = new CanonicalInstrumentId(market.BaseAsset, market.QuoteAsset);
    var now = DateTimeOffset.UtcNow;
    FixedPoint buyAsk;
    FixedPoint buyQuantity;
    FixedPoint sellBid;
    FixedPoint sellQuantity;
    try
    {
        buyAsk = FixedPoint.Parse(request.BuyAskPrice, market.Binance.Precision.PriceScale, nameof(request.BuyAskPrice));
        buyQuantity = FixedPoint.Parse(request.BuyAskQuantity, market.Binance.Precision.QuantityScale, nameof(request.BuyAskQuantity));
        sellBid = FixedPoint.Parse(request.SellBidPrice, market.Coinbase.Precision.PriceScale, nameof(request.SellBidPrice));
        sellQuantity = FixedPoint.Parse(request.SellBidQuantity, market.Coinbase.Precision.QuantityScale, nameof(request.SellBidQuantity));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }

    var buyBook = CreateBook(
        Exchange.BinanceSpot,
        instrument,
        now,
        new FixedPoint(buyAsk.Units - market.Binance.Precision.PriceTickUnits),
        buyAsk,
        buyQuantity);
    var sellBook = CreateBook(
        Exchange.CoinbaseAdvancedTrade,
        instrument,
        now,
        sellBid,
        new FixedPoint(sellBid.Units + market.Coinbase.Precision.PriceTickUnits),
        sellQuantity);
    var opportunity = SpreadOpportunityCalculator.Calculate(buyBook, sellBook, now, options.Value.Book.Freshness);

    if (opportunity is null)
    {
        return Results.UnprocessableEntity(new { error = "No fresh, profitable opportunity was found." });
    }

    store.Save(opportunity);
    return Results.Ok(ToResponse(opportunity, market.Binance.Precision.PriceScale, market.Binance.Precision.QuantityScale));
});

app.MapGet("/api/opportunities/latest", (
    IOptions<MarketDataOptions> options,
    ISpreadOpportunityStore store) =>
{
    var market = options.Value.Markets.Single();
    var instrument = new CanonicalInstrumentId(market.BaseAsset, market.QuoteAsset);
    var opportunity = store.GetLatest(instrument);
    return opportunity is null
        ? Results.NotFound(new { error = "No opportunity has been persisted for the configured market." })
        : Results.Ok(ToResponse(opportunity, market.Binance.Precision.PriceScale, market.Binance.Precision.QuantityScale));
});

app.Run();

static BookView CreateBook(
    Exchange exchange,
    CanonicalInstrumentId instrument,
    DateTimeOffset receivedAtUtc,
    FixedPoint bestBid,
    FixedPoint bestAsk,
    FixedPoint quantity) => new(
    exchange,
    instrument,
    BookStatus.Valid,
    BookInvalidationReason.None,
    receivedAtUtc,
    0,
    1,
    1,
    new BookLevel(bestBid, quantity),
    new BookLevel(bestAsk, quantity));

static object ToResponse(SpreadOpportunity opportunity, int priceScale, int quantityScale) => new
{
    opportunity.Instrument,
    buyExchange = opportunity.BuyExchange.ToString(),
    sellExchange = opportunity.SellExchange.ToString(),
    opportunity.ObservedAtUtc,
    buyAsk = opportunity.BuyAsk.ToDisplayString(priceScale),
    sellBid = opportunity.SellBid.ToDisplayString(priceScale),
    quantity = opportunity.Quantity.ToDisplayString(quantityScale),
    grossSpread = opportunity.GrossSpread.ToDisplayString(priceScale),
    opportunity.GrossSpreadBps
};

public partial class Program;
