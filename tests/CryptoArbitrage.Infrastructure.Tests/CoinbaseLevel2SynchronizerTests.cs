using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Coinbase;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class CoinbaseLevel2SynchronizerTests
{
    [Fact]
    public void SnapshotMakesBookValidAndExportsCaptureProvenance()
    {
        var synchronizer = Create(retainedDepth: 2);
        var receipt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        var eventTime = receipt.AddMilliseconds(-1);
        synchronizer.BeginSynchronization(1);

        Assert.True(synchronizer.ApplySnapshot(new CoinbaseLevel2Snapshot(100,
            [Level(10000, 100), Level(10002, 100), Level(10001, 100)],
            [Level(10005, 100), Level(10003, 100), Level(10004, 100)], receipt, 17, eventTime)));

        var snapshot = synchronizer.ExportSnapshot();
        Assert.Equal(Exchange.CoinbaseAdvancedTrade, snapshot.Exchange);
        Assert.Equal(BookStatus.Valid, snapshot.Status);
        Assert.Equal(100, snapshot.FinalSequence);
        Assert.Equal(receipt, snapshot.LastAppliedReceivedAtUtc);
        Assert.Equal(17, snapshot.LastAppliedStopwatchTicks);
        Assert.Equal(eventTime, snapshot.SourceEventAtUtc);
        Assert.Equal([10002L, 10001L], snapshot.Bids.Select(level => level.Price.Units));
        Assert.Equal([10003L, 10004L], snapshot.Asks.Select(level => level.Price.Units));
    }

    [Fact]
    public void ExactNextSequenceAppliesReplacementAndZeroRemoval()
    {
        var synchronizer = Create();
        synchronizer.BeginSynchronization(1);
        Assert.True(synchronizer.ApplySnapshot(Snapshot(100)));

        var receipt = new DateTimeOffset(2026, 9, 2, 10, 0, 1, TimeSpan.Zero);
        Assert.True(synchronizer.ApplyLive(Update(101,
            new CoinbaseLevel2Change(BookSide.Bid, Level(10001, 333), receipt.AddMilliseconds(-2)),
            new CoinbaseLevel2Change(BookSide.Ask, Level(10002, 0), receipt.AddMilliseconds(-1)), receipt, 20)));

        var snapshot = synchronizer.ExportSnapshot();
        Assert.Equal(101, snapshot.FinalSequence);
        Assert.Equal(receipt, snapshot.LastAppliedReceivedAtUtc);
        Assert.Equal(10001, snapshot.Bids[0].Price.Units);
        Assert.Equal(333, snapshot.Bids[0].Quantity.Units);
        Assert.Equal(10003, snapshot.Asks[0].Price.Units);
        Assert.Equal(receipt.AddMilliseconds(-1), snapshot.SourceEventAtUtc);
    }

    [Fact]
    public void SequenceGapInvalidatesAndClearsExport()
    {
        var synchronizer = Create();
        synchronizer.BeginSynchronization(1);
        Assert.True(synchronizer.ApplySnapshot(Snapshot(100)));

        Assert.False(synchronizer.ApplyLive(Update(102)));
        var snapshot = synchronizer.ExportSnapshot();
        Assert.Equal(BookStatus.Invalid, snapshot.Status);
        Assert.Equal(BookInvalidationReason.SequenceGap, snapshot.InvalidationReason);
        Assert.Empty(snapshot.Bids);
        Assert.Null(snapshot.FinalSequence);
    }

    [Fact]
    public void ReconnectRequiresAReplacementSnapshot()
    {
        var synchronizer = Create();
        synchronizer.BeginSynchronization(1);
        Assert.True(synchronizer.ApplySnapshot(Snapshot(100)));

        synchronizer.BeginSynchronization(2);
        Assert.False(synchronizer.ApplyLive(Update(101)));
        Assert.True(synchronizer.ApplySnapshot(new CoinbaseLevel2Snapshot(9,
            [Level(20000, 100)], [Level(20001, 100)], DateTimeOffset.UtcNow, 5)));

        var snapshot = synchronizer.ExportSnapshot();
        Assert.Equal(2, snapshot.ConnectionEpoch);
        Assert.Equal(9, snapshot.FinalSequence);
        Assert.Equal(20000, snapshot.Bids[0].Price.Units);
    }

    [Fact]
    public void CrossedSnapshotFailsClosed()
    {
        var synchronizer = Create();
        synchronizer.BeginSynchronization(1);

        Assert.False(synchronizer.ApplySnapshot(new CoinbaseLevel2Snapshot(100,
            [Level(10002, 100)], [Level(10001, 100)], DateTimeOffset.UtcNow, 1)));
        Assert.Equal(BookStatus.Invalid, synchronizer.Status);
        Assert.Equal(BookInvalidationReason.SnapshotMismatch, synchronizer.InvalidationReason);
        Assert.Empty(synchronizer.ExportSnapshot().Bids);
    }

    private static CoinbaseLevel2Synchronizer Create(int retainedDepth = 100) => new(new CanonicalInstrumentId("BTC", "USDT"), retainedDepth);
    private static CoinbaseLevel2Snapshot Snapshot(long sequence) => new(sequence,
        [Level(10000, 200), Level(9999, 100)], [Level(10002, 150), Level(10003, 300)], DateTimeOffset.UtcNow, 1);
    private static CoinbaseLevel2Update Update(long sequence, params CoinbaseLevel2Change[] changes) => new(sequence, changes, DateTimeOffset.UtcNow, 1);
    private static CoinbaseLevel2Update Update(long sequence, CoinbaseLevel2Change first, CoinbaseLevel2Change second, DateTimeOffset receivedAtUtc, long ticks) => new(sequence, [first, second], receivedAtUtc, ticks);
    private static BookLevel Level(long price, long quantity) => new(new FixedPoint(price), new FixedPoint(quantity));
}
