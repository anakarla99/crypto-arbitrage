using System.Text;
using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Coinbase;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class CoinbaseLevel2ParserTests
{
    [Fact]
    public void ParsesSnapshotAndPreservesReceiptAndEventMetadata()
    {
        var parser = CreateParser();
        var receivedAt = new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero);

        var deltas = parser.Parse(Encoding.UTF8.GetBytes("""
            {
              "channel": "l2_data",
              "sequence_num": 10,
              "events": [{
                "type": "snapshot",
                "product_id": "BTC-USDT",
                "event_time": "2026-10-09T14:59:59.900Z",
                "updates": [
                  {"side": "bid", "price_level": "65000.00", "new_quantity": "0.01000000"},
                  {"side": "ask", "price_level": "65001.00", "new_quantity": "0.02000000"}
                ]
              }]
            }
            """), receivedAt, 42);

        var delta = Assert.Single(deltas);
        Assert.Equal(BookUpdateKind.Snapshot, delta.Kind);
        Assert.Equal(Exchange.CoinbaseAdvancedTrade, delta.Exchange);
        Assert.Equal(receivedAt, delta.ReceivedAtUtc);
        Assert.Equal(42, delta.ReceivedAtStopwatchTicks);
        Assert.Equal(6500000000000, delta.Bids[0].Price.Units);
        Assert.Equal(1000000, delta.Bids[0].Quantity.Units);
        Assert.Equal(6500100000000, delta.Asks[0].Price.Units);
        Assert.Equal(2000000, delta.Asks[0].Quantity.Units);
    }

    [Fact]
    public void ParsesZeroQuantityDeletesAsDeltas()
    {
        var parser = CreateParser();
        var deltas = parser.Parse(Encoding.UTF8.GetBytes("""
            {
              "channel": "l2_data",
              "sequence_num": 11,
              "events": [{
                "type": "update",
                "product_id": "BTC-USDT",
                "event_time": "2026-10-09T15:00:00Z",
                "updates": [
                  {"side": "bid", "price_level": "65000.00", "new_quantity": "0"}
                ]
              }]
            }
            """), DateTimeOffset.UtcNow, 1);

        var delta = Assert.Single(deltas);
        Assert.Equal(BookUpdateKind.Delta, delta.Kind);
        Assert.Equal(0, delta.Bids[0].Quantity.Units);
    }

    [Fact]
    public void RejectsUnexpectedProductOrUnalignedPrice()
    {
        var parser = CreateParser();
        var wrongProduct = """{"channel":"l2_data","sequence_num":10,"events":[{"type":"update","product_id":"BTC-USD","event_time":"2026-10-09T15:00:00Z","updates":[]}]}""";
        var unalignedPrice = """{"channel":"l2_data","sequence_num":10,"events":[{"type":"update","product_id":"BTC-USDT","event_time":"2026-10-09T15:00:00Z","updates":[{"side":"bid","price_level":"65000.001","new_quantity":"0.01"}]}]}""";

        Assert.Throws<FormatException>(() => parser.Parse(Encoding.UTF8.GetBytes(wrongProduct), DateTimeOffset.UtcNow, 1));
        Assert.Throws<ArgumentException>(() => parser.Parse(Encoding.UTF8.GetBytes(unalignedPrice), DateTimeOffset.UtcNow, 1));
    }

    private static CoinbaseLevel2Parser CreateParser() => new(
        new CanonicalInstrumentId("BTC", "USDT"),
        new VenueInstrument(
            Exchange.CoinbaseAdvancedTrade,
            "BTC-USDT",
            new VenuePrecision(8, 1_000_000, 8, 1)));
}
