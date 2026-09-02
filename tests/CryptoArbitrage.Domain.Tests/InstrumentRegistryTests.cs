using CryptoArbitrage.Domain;
using Xunit;

namespace CryptoArbitrage.Domain.Tests;

public sealed class InstrumentRegistryTests
{
    [Fact]
    public void ResolvesBothVenueSymbolsToTheSameCanonicalInstrument()
    {
        var registry = CreateRegistry();

        Assert.Equal("BTC-USDT", registry.Resolve(Exchange.BinanceSpot, "BTCUSDT").Id.Value);
        Assert.Equal("BTC-USDT", registry.Resolve(Exchange.CoinbaseAdvancedTrade, "BTC-USDT").Id.Value);
    }

    [Fact]
    public void RejectsDuplicateVenueMappings()
    {
        var id = new CanonicalInstrumentId("BTC", "USDT");
        var first = new InstrumentDefinition(id, [Venue(Exchange.BinanceSpot, "BTCUSDT")]);
        var second = new InstrumentDefinition(new CanonicalInstrumentId("ETH", "USDT"), [Venue(Exchange.BinanceSpot, "BTCUSDT")]);

        Assert.Throws<ArgumentException>(() => new InstrumentRegistry([first, second]));
    }

    [Fact]
    public void TreatsDifferentQuoteCurrenciesAsNotDirectlyComparable()
    {
        Assert.False(InstrumentRegistry.AreDirectlyComparable(new CanonicalInstrumentId("BTC", "USD"), new CanonicalInstrumentId("BTC", "USDT")));
    }

    [Fact]
    public void DoesNotGuessUnknownVenueSymbols()
    {
        Assert.Throws<KeyNotFoundException>(() => CreateRegistry().Resolve(Exchange.BinanceSpot, "BTC-USDT"));
    }

    private static InstrumentRegistry CreateRegistry() => new(
    [
        new InstrumentDefinition(new CanonicalInstrumentId("BTC", "USDT"),
        [
            Venue(Exchange.BinanceSpot, "BTCUSDT"),
            Venue(Exchange.CoinbaseAdvancedTrade, "BTC-USDT")
        ])
    ]);

    private static VenueInstrument Venue(Exchange exchange, string symbol) => new(exchange, symbol, new VenuePrecision(8, 1_000_000, 8, 1));
}
