using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Binance;
using CryptoArbitrage.Infrastructure.Transport;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class MarketDataOrchestratorTests
{
    [Fact]
    public async Task AppliesRestSnapshotToBufferedBinanceUpdates()
    {
        var instrument = new CanonicalInstrumentId("BTC", "USDT");
        var synchronizer = new BinanceDepthSynchronizer(instrument, 10);
        synchronizer.BeginSynchronization(1);
        synchronizer.Buffer(new BinanceDepthUpdate(
            101,
            101,
            [Level(65000, 100)],
            [Level(65001, 100)],
            DateTimeOffset.UtcNow,
            1));
        var quality = new QualitySink();
        var orchestrator = new MarketDataOrchestrator(
            synchronizer,
            new SnapshotClient(new BinanceDepthSnapshot(100, [Level(64999, 100)], [Level(65002, 100)])),
            quality,
            "BTCUSDT",
            500);

        Assert.True(await orchestrator.SynchronizeBinanceAsync(DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(BookStatus.Valid, synchronizer.Status);
        Assert.Equal(101, synchronizer.ExportSnapshot().FinalSequence);
        Assert.Empty(quality.Events);
    }

    [Fact]
    public async Task PublishesQualityEventWhenSnapshotCannotBridgeUpdates()
    {
        var instrument = new CanonicalInstrumentId("BTC", "USDT");
        var synchronizer = new BinanceDepthSynchronizer(instrument, 10);
        synchronizer.BeginSynchronization(1);
        synchronizer.Buffer(new BinanceDepthUpdate(
            105,
            105,
            [],
            [],
            DateTimeOffset.UtcNow,
            1));
        var quality = new QualitySink();
        var orchestrator = new MarketDataOrchestrator(
            synchronizer,
            new SnapshotClient(new BinanceDepthSnapshot(100, [Level(64999, 100)], [Level(65002, 100)])),
            quality,
            "BTCUSDT",
            500);

        Assert.False(await orchestrator.SynchronizeBinanceAsync(DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(QualityEventType.SnapshotMismatch, Assert.Single(quality.Events).Type);
    }

    private static BookLevel Level(long price, long quantity) =>
        new(new FixedPoint(price), new FixedPoint(quantity));

    private sealed class SnapshotClient(BinanceDepthSnapshot snapshot) : IBinanceDepthSnapshotClient
    {
        public Task<BinanceDepthSnapshot> GetSnapshotAsync(string symbol, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
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
