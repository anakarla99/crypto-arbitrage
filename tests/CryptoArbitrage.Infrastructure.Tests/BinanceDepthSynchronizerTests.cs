using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Binance;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class BinanceDepthSynchronizerTests
{
    [Fact]
    public void BridgesSnapshotAppliesUpdatesAndRemovesZeroQuantityLevels()
    {
        var synchronizer = Create();
        synchronizer.Buffer(Update(100, 102, [Level(10000, 125)], [Level(10002, 0), Level(10001, 200)]));

        Assert.True(synchronizer.ApplySnapshot(Snapshot(100)));
        var view = synchronizer.GetView(DateTimeOffset.UtcNow, 1);

        Assert.Equal(BookStatus.Valid, view.Status);
        Assert.Equal(102, view.FinalSequence);
        Assert.Equal(10000, view.BestBid!.Value.Price.Units);
        Assert.Equal(10001, view.BestAsk!.Value.Price.Units);
    }

    [Fact]
    public void InvalidatesOnALiveSequenceGap()
    {
        var synchronizer = Create();
        synchronizer.Buffer(Update(100, 102, [], []));
        Assert.True(synchronizer.ApplySnapshot(Snapshot(100)));

        Assert.False(synchronizer.ApplyLive(Update(104, 104, [], [])));
        Assert.Equal(BookStatus.Invalid, synchronizer.Status);
        Assert.Equal(BookInvalidationReason.SequenceGap, synchronizer.InvalidationReason);
    }

    [Fact]
    public void RejectsSnapshotWithoutABridgeUpdate()
    {
        var synchronizer = Create();
        synchronizer.Buffer(Update(104, 104, [], []));

        Assert.False(synchronizer.ApplySnapshot(Snapshot(100)));
        Assert.Equal(BookInvalidationReason.SnapshotMismatch, synchronizer.InvalidationReason);
    }

    private static BinanceDepthSynchronizer Create() => new(new CanonicalInstrumentId("BTC", "USDT"), 100);
    private static BinanceDepthSnapshot Snapshot(long id) => new(id, [Level(10000, 200), Level(9999, 100)], [Level(10002, 150), Level(10003, 300)]);
    private static BinanceDepthUpdate Update(long first, long last, IReadOnlyList<BookLevel> bids, IReadOnlyList<BookLevel> asks) => new(first, last, bids, asks, DateTimeOffset.UtcNow, 1);
    private static BookLevel Level(long price, long quantity) => new(new FixedPoint(price), new FixedPoint(quantity));
}
