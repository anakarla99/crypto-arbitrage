using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Transport;
using Xunit;

namespace CryptoArbitrage.Infrastructure.Tests;

public sealed class MarketDataStreamCoordinatorTests
{
    [Fact]
    public async Task RunsBothVenueLifecyclesTogether()
    {
        var completed = new List<Exchange>();
        var coordinator = new MarketDataStreamCoordinator(
            _ =>
            {
                completed.Add(Exchange.BinanceSpot);
                return Task.CompletedTask;
            },
            _ =>
            {
                completed.Add(Exchange.CoinbaseAdvancedTrade);
                return Task.CompletedTask;
            });

        await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(
            [Exchange.BinanceSpot, Exchange.CoinbaseAdvancedTrade],
            completed.OrderBy(exchange => exchange));
    }

    [Fact]
    public async Task StateSinkStartsNewEpochOnEveryConnection()
    {
        var quality = new QualitySink();
        var epochs = new List<(Exchange Exchange, long Epoch)>();
        var sink = new MarketDataConnectionStateSink(quality, (exchange, epoch) => epochs.Add((exchange, epoch)));
        var at = DateTimeOffset.UtcNow;

        await sink.PublishAsync(
            new ConnectionStateChanged(
                Exchange.BinanceSpot,
                ConnectionState.Stopped,
                ConnectionState.Connecting,
                ConnectionStateReason.Started,
                0,
                at),
            CancellationToken.None);
        await sink.PublishAsync(
            new ConnectionStateChanged(
                Exchange.BinanceSpot,
                ConnectionState.BackingOff,
                ConnectionState.Connecting,
                ConnectionStateReason.Started,
                1,
                at),
            CancellationToken.None);

        Assert.Equal([(Exchange.BinanceSpot, 1L), (Exchange.BinanceSpot, 2L)], epochs);
        Assert.Equal(2, quality.Events.Count);
        Assert.All(quality.Events, qualityEvent => Assert.Equal(QualityEventType.ConnectionStateChanged, qualityEvent.Type));
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
