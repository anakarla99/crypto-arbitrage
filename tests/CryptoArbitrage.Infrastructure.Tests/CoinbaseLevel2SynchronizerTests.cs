using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Coinbase;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class CoinbaseLevel2SynchronizerTests
{
    [Fact]
    public void AppliesSnapshotUpdatesAndZeroQuantityDeletions()
    {
        var synchronizer = Create();
        synchronizer.BeginSynchronization(1);
        Assert.True(synchronizer.ApplySnapshot(Update(10, BookUpdateKind.Snapshot,
            [Level(10003, 100), Level(10002, 100)],
            [Level(10004, 100), Level(10005, 100)])));

        Assert.True(synchronizer.ApplyLive(Update(11, BookUpdateKind.Delta, [Level(10003, 0)], [])));
        var snapshot = synchronizer.ExportSnapshot();

        Assert.Equal(BookStatus.Valid, snapshot.Status);
        Assert.Equal(11, snapshot.FinalSequence);
        Assert.Equal([10002L], snapshot.Bids.Select(level => level.Price.Units));
        Assert.Equal([10004L, 10005L], snapshot.Asks.Select(level => level.Price.Units));
    }

    [Fact]
    public void InvalidatesOnSequenceGapAndClearsBook()
    {
        var synchronizer = Create();
        synchronizer.BeginSynchronization(1);
        Assert.True(synchronizer.ApplySnapshot(Update(10, BookUpdateKind.Snapshot, [Level(10000, 100)], [Level(10002, 100)])));

        Assert.False(synchronizer.ApplyLive(Update(12, BookUpdateKind.Delta, [], [])));

        var snapshot = synchronizer.ExportSnapshot();
        Assert.Equal(BookStatus.Invalid, snapshot.Status);
        Assert.Equal(BookInvalidationReason.SequenceGap, snapshot.InvalidationReason);
        Assert.Empty(snapshot.Bids);
        Assert.Null(snapshot.FinalSequence);
    }

    [Fact]
    public void ReconnectDoesNotExposePriorMarketData()
    {
        var synchronizer = Create();
        synchronizer.BeginSynchronization(1);
        Assert.True(synchronizer.ApplySnapshot(Update(10, BookUpdateKind.Snapshot, [Level(10000, 100)], [Level(10002, 100)])));

        synchronizer.BeginSynchronization(2);
        var snapshot = synchronizer.ExportSnapshot();

        Assert.Equal(BookStatus.Synchronizing, snapshot.Status);
        Assert.Equal(2, snapshot.ConnectionEpoch);
        Assert.Empty(snapshot.Bids);
        Assert.Null(snapshot.FinalSequence);
    }

    [Fact]
    public void RejectsSnapshotThatDoesNotProduceAValidBook()
    {
        var synchronizer = Create();
        synchronizer.BeginSynchronization(1);

        Assert.False(synchronizer.ApplySnapshot(Update(10, BookUpdateKind.Snapshot, [Level(10002, 100)], [Level(10001, 100)])));
        Assert.Equal(BookInvalidationReason.SnapshotMismatch, synchronizer.InvalidationReason);
    }

    private static CoinbaseLevel2Synchronizer Create() =>
        new(new CanonicalInstrumentId("BTC", "USDT"), 2);

    private static CoinbaseLevel2Update Update(
        long sequence,
        BookUpdateKind kind,
        IReadOnlyList<BookLevel> bids,
        IReadOnlyList<BookLevel> asks) =>
        new(
            Exchange.CoinbaseAdvancedTrade,
            new CanonicalInstrumentId("BTC", "USDT"),
            DateTimeOffset.UtcNow,
            1,
            DateTimeOffset.UtcNow,
            sequence,
            kind,
            bids,
            asks);

    private static BookLevel Level(long price, long quantity) =>
        new(new FixedPoint(price), new FixedPoint(quantity));
}
