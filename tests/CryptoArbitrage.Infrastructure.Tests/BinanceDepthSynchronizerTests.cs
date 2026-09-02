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

    [Fact]
    public void ExportsCompleteRetainedDepthInMarketOrder()
    {
        var synchronizer = new BinanceDepthSynchronizer(new CanonicalInstrumentId("BTC", "USDT"), retainedDepth: 2);
        synchronizer.BeginSynchronization(1);
        synchronizer.Buffer(Update(100, 100, [Level(10003, 100), Level(10002, 100)], [Level(10004, 100), Level(10005, 100)]));

        Assert.True(synchronizer.ApplySnapshot(new BinanceDepthSnapshot(99,
            [Level(10000, 100), Level(10001, 100), Level(9999, 100)],
            [Level(10006, 100), Level(10007, 100), Level(10008, 100)])));

        var snapshot = synchronizer.ExportSnapshot();
        Assert.Equal(BookStatus.Valid, snapshot.Status);
        Assert.Equal(1, snapshot.ConnectionEpoch);
        Assert.Equal([10003L, 10002L], snapshot.Bids.Select(level => level.Price.Units));
        Assert.Equal([10004L, 10005L], snapshot.Asks.Select(level => level.Price.Units));
    }

    [Fact]
    public void ExportUsesLastAppliedReceiptMetadataAndIgnoresDuplicateUpdates()
    {
        var synchronizer = Create();
        var bridgeTime = new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);
        synchronizer.BeginSynchronization(1);
        synchronizer.Buffer(Update(100, 100, [], [], bridgeTime, 10));
        Assert.True(synchronizer.ApplySnapshot(Snapshot(99)));
        Assert.Equal(bridgeTime, synchronizer.ExportSnapshot().LastAppliedReceivedAtUtc);
        Assert.Equal(10, synchronizer.ExportSnapshot().LastAppliedStopwatchTicks);

        var liveTime = bridgeTime.AddMilliseconds(5);
        Assert.True(synchronizer.ApplyLive(Update(101, 101, [], [], liveTime, 20)));
        Assert.True(synchronizer.ApplyLive(Update(101, 101, [], [], liveTime.AddMilliseconds(5), 30)));
        var exported = synchronizer.ExportSnapshot();
        Assert.Equal(101, exported.FinalSequence);
        Assert.Equal(liveTime, exported.LastAppliedReceivedAtUtc);
        Assert.Equal(20, exported.LastAppliedStopwatchTicks);
    }

    [Fact]
    public void ReconnectAndInvalidationDoNotExposePriorMarketData()
    {
        var synchronizer = Create();
        synchronizer.BeginSynchronization(1);
        synchronizer.Buffer(Update(100, 100, [], []));
        Assert.True(synchronizer.ApplySnapshot(Snapshot(99)));

        synchronizer.BeginSynchronization(2);
        var reconnecting = synchronizer.ExportSnapshot();
        Assert.Equal(BookStatus.Synchronizing, reconnecting.Status);
        Assert.Equal(2, reconnecting.ConnectionEpoch);
        Assert.Empty(reconnecting.Bids);
        Assert.Null(reconnecting.FinalSequence);

        synchronizer.Buffer(Update(100, 100, [], []));
        Assert.True(synchronizer.ApplySnapshot(Snapshot(99)));
        Assert.False(synchronizer.ApplyLive(Update(102, 102, [], [])));
        var invalid = synchronizer.ExportSnapshot();
        Assert.Equal(BookStatus.Invalid, invalid.Status);
        Assert.Equal(BookInvalidationReason.SequenceGap, invalid.InvalidationReason);
        Assert.Empty(invalid.Bids);
        Assert.Null(invalid.LastAppliedReceivedAtUtc);
    }

    private static BinanceDepthSynchronizer Create() => new(new CanonicalInstrumentId("BTC", "USDT"), 100);
    private static BinanceDepthSnapshot Snapshot(long id) => new(id, [Level(10000, 200), Level(9999, 100)], [Level(10002, 150), Level(10003, 300)]);
    private static BinanceDepthUpdate Update(long first, long last, IReadOnlyList<BookLevel> bids, IReadOnlyList<BookLevel> asks, DateTimeOffset? receivedAtUtc = null, long stopwatchTicks = 1) => new(first, last, bids, asks, receivedAtUtc ?? DateTimeOffset.UtcNow, stopwatchTicks);
    private static BookLevel Level(long price, long quantity) => new(new FixedPoint(price), new FixedPoint(quantity));
}
