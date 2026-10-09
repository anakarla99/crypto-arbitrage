using System.Text;
using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Binance;
using CryptoArbitrage.Infrastructure.Coinbase;
using CryptoArbitrage.Infrastructure.Transport;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class MarketDataTransportTests
{
    [Fact]
    public async Task RouterPublishesParseRejectionWithoutThrowing()
    {
        var quality = new QualitySink();
        var router = CreateRouter(quality);

        await router.HandleAsync(
            Exchange.BinanceSpot,
            Encoding.UTF8.GetBytes("""{"e":"depthUpdate","s":"BTCUSDT"}"""),
            DateTimeOffset.UtcNow,
            1,
            CancellationToken.None);

        var eventRecord = Assert.Single(quality.Events);
        Assert.Equal(QualityEventType.ParseRejection, eventRecord.Type);
        Assert.Equal(Exchange.BinanceSpot, eventRecord.Exchange);
    }

    [Fact]
    public async Task RouterAppliesCoinbaseSnapshotAndUpdate()
    {
        var quality = new QualitySink();
        var router = CreateRouter(quality);
        var time = "2026-10-09T15:00:00Z";

        await router.HandleAsync(Exchange.CoinbaseAdvancedTrade, Encoding.UTF8.GetBytes(
            $$"""{"channel":"l2_data","sequence_num":10,"events":[{"type":"snapshot","product_id":"BTC-USDT","event_time":"{{time}}","updates":[{"side":"bid","price_level":"65000.00","new_quantity":"0.01"},{"side":"bid","price_level":"64999.00","new_quantity":"0.01"},{"side":"ask","price_level":"65001.00","new_quantity":"0.01"}]}]}"""),
            DateTimeOffset.UtcNow, 1, CancellationToken.None);
        await router.HandleAsync(Exchange.CoinbaseAdvancedTrade, Encoding.UTF8.GetBytes(
            $$"""{"channel":"l2_data","sequence_num":11,"events":[{"type":"update","product_id":"BTC-USDT","event_time":"{{time}}","updates":[{"side":"bid","price_level":"65000.00","new_quantity":"0"}]}]}"""),
            DateTimeOffset.UtcNow, 2, CancellationToken.None);

        Assert.Empty(quality.Events);
    }

    private static MarketDataFrameRouter CreateRouter(QualitySink quality)
    {
        var instrument = new CanonicalInstrumentId("BTC", "USDT");
        var binanceVenue = new VenueInstrument(Exchange.BinanceSpot, "BTCUSDT", new VenuePrecision(8, 1_000_000, 8, 1_000));
        var coinbaseVenue = new VenueInstrument(Exchange.CoinbaseAdvancedTrade, "BTC-USDT", new VenuePrecision(8, 1_000_000, 8, 1));
        var binance = new BinanceDepthSynchronizer(instrument, 100);
        var coinbase = new CoinbaseLevel2Synchronizer(instrument, 100);
        binance.BeginSynchronization(1);
        coinbase.BeginSynchronization(1);
        return new MarketDataFrameRouter(
            new BinanceDepthParser(instrument, binanceVenue),
            binance,
            new CoinbaseLevel2Parser(instrument, coinbaseVenue),
            coinbase,
            quality);
    }

    private sealed class QualitySink : IMarketDataQualitySink
    {
        public List<MarketDataQualityEvent> Events { get; } = [];

        public ValueTask PublishAsync(MarketDataQualityEvent qualityEvent, CancellationToken cancellationToken)
        {
            Events.Add(qualityEvent);
            return ValueTask.CompletedTask;
        }
    }
}
