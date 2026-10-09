using System.Text;
using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Binance;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class BinanceDepthParserTests
{
    [Fact]
    public void ParsesDepthUpdateAndPreservesMetadata()
    {
        var parser = CreateParser();
        var receivedAt = new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero);

        var update = parser.Parse(Encoding.UTF8.GetBytes("""
            {
              "e": "depthUpdate",
              "E": 1791558000000,
              "s": "BTCUSDT",
              "U": 101,
              "u": 105,
              "b": [["65000.00", "0.01000000"]],
              "a": [["65001.00", "0.02000000"]]
            }
            """), receivedAt, 42);

        Assert.Equal(101, update.FirstUpdateId);
        Assert.Equal(105, update.FinalUpdateId);
        Assert.Equal(receivedAt, update.ReceivedAtUtc);
        Assert.Equal(42, update.ReceivedAtStopwatchTicks);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1791558000000), update.ExchangeEventAtUtc);
        Assert.Equal(6500000000000, update.Bids[0].Price.Units);
        Assert.Equal(1000000, update.Bids[0].Quantity.Units);
        Assert.Equal(6500100000000, update.Asks[0].Price.Units);
        Assert.Equal(2000000, update.Asks[0].Quantity.Units);
    }

    [Fact]
    public void ParsesZeroQuantityAsADeletion()
    {
        var update = CreateParser().Parse(Encoding.UTF8.GetBytes("""
            {
              "e": "depthUpdate",
              "E": 1791558000000,
              "s": "BTCUSDT",
              "U": 10,
              "u": 10,
              "b": [["65000.00", "0"]],
              "a": []
            }
            """), DateTimeOffset.UtcNow, 1);

        Assert.Equal(0, Assert.Single(update.Bids).Quantity.Units);
    }

    [Theory]
    [InlineData("""{"e":"trade","E":1791558000000,"s":"BTCUSDT","U":10,"u":10,"b":[],"a":[]}""")]
    [InlineData("""{"e":"depthUpdate","E":1791558000000,"s":"BTCUSD","U":10,"u":10,"b":[],"a":[]}""")]
    [InlineData("""{"e":"depthUpdate","E":1791558000000,"s":"BTCUSDT","U":11,"u":10,"b":[],"a":[]}""")]
    [InlineData("""{"e":"depthUpdate","E":1791558000000,"s":"BTCUSDT","U":10,"u":10,"b":[]""")]
    public void RejectsMalformedOrUnexpectedMessages(string payload)
    {
        Assert.ThrowsAny<Exception>(() => CreateParser().Parse(Encoding.UTF8.GetBytes(payload), DateTimeOffset.UtcNow, 1));
    }

    [Fact]
    public void RejectsUnalignedPriceAndMissingLevelValues()
    {
        var parser = CreateParser();
        var unalignedPrice = """{"e":"depthUpdate","E":1791558000000,"s":"BTCUSDT","U":10,"u":10,"b":[["65000.001","0.01"]],"a":[]}""";
        var missingQuantity = """{"e":"depthUpdate","E":1791558000000,"s":"BTCUSDT","U":10,"u":10,"b":[["65000.00"]],"a":[]}""";

        Assert.Throws<ArgumentException>(() => parser.Parse(Encoding.UTF8.GetBytes(unalignedPrice), DateTimeOffset.UtcNow, 1));
        Assert.Throws<FormatException>(() => parser.Parse(Encoding.UTF8.GetBytes(missingQuantity), DateTimeOffset.UtcNow, 1));
    }

    private static BinanceDepthParser CreateParser() => new(
        new CanonicalInstrumentId("BTC", "USDT"),
        new VenueInstrument(
            Exchange.BinanceSpot,
            "BTCUSDT",
            new VenuePrecision(8, 1_000_000, 8, 1_000)));
}
