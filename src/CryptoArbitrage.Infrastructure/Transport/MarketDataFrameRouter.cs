using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Binance;
using CryptoArbitrage.Infrastructure.Coinbase;

namespace CryptoArbitrage.Infrastructure.Transport;

public enum QualityEventType { ParseRejection, SequenceGap, SnapshotMismatch }

public readonly record struct MarketDataQualityEvent(
    Exchange Exchange,
    CanonicalInstrumentId Instrument,
    QualityEventType Type,
    DateTimeOffset OccurredAtUtc,
    long ConnectionEpoch,
    long? LastSequence,
    int PayloadBytes,
    string Details);

public interface IMarketDataQualitySink
{
    ValueTask PublishAsync(MarketDataQualityEvent qualityEvent, CancellationToken cancellationToken);
}

public sealed class MarketDataFrameRouter : IInboundFrameSink
{
    private readonly BinanceDepthParser _binanceParser;
    private readonly BinanceDepthSynchronizer _binanceSynchronizer;
    private readonly CoinbaseLevel2Parser _coinbaseParser;
    private readonly CoinbaseLevel2Synchronizer _coinbaseSynchronizer;
    private readonly IMarketDataQualitySink _qualitySink;

    public MarketDataFrameRouter(
        BinanceDepthParser binanceParser,
        BinanceDepthSynchronizer binanceSynchronizer,
        CoinbaseLevel2Parser coinbaseParser,
        CoinbaseLevel2Synchronizer coinbaseSynchronizer,
        IMarketDataQualitySink qualitySink)
    {
        _binanceParser = binanceParser ?? throw new ArgumentNullException(nameof(binanceParser));
        _binanceSynchronizer = binanceSynchronizer ?? throw new ArgumentNullException(nameof(binanceSynchronizer));
        _coinbaseParser = coinbaseParser ?? throw new ArgumentNullException(nameof(coinbaseParser));
        _coinbaseSynchronizer = coinbaseSynchronizer ?? throw new ArgumentNullException(nameof(coinbaseSynchronizer));
        _qualitySink = qualitySink ?? throw new ArgumentNullException(nameof(qualitySink));
    }

    public async ValueTask HandleAsync(
        Exchange exchange,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset receivedAtUtc,
        long receivedAtStopwatchTicks,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (exchange)
            {
                case Exchange.BinanceSpot:
                    _binanceSynchronizer.Buffer(_binanceParser.Parse(payload, receivedAtUtc, receivedAtStopwatchTicks));
                    break;
                case Exchange.CoinbaseAdvancedTrade:
                    foreach (var update in _coinbaseParser.ParseUpdates(payload, receivedAtUtc, receivedAtStopwatchTicks))
                    {
                        var applied = update.Kind == BookUpdateKind.Snapshot
                            ? _coinbaseSynchronizer.ApplySnapshot(update)
                            : _coinbaseSynchronizer.ApplyLive(update);
                        if (!applied)
                        {
                            await PublishAsync(
                                exchange,
                                _coinbaseSynchronizer.InvalidationReason == BookInvalidationReason.SequenceGap
                                    ? QualityEventType.SequenceGap
                                    : QualityEventType.SnapshotMismatch,
                                receivedAtUtc,
                                _coinbaseSynchronizer.ConnectionEpoch,
                                _coinbaseSynchronizer.ExportSnapshot().FinalSequence,
                                payload.Length,
                                "Coinbase update could not be applied.",
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(exchange));
            }
        }
        catch (FormatException exception)
        {
            await PublishAsync(
                exchange,
                QualityEventType.ParseRejection,
                receivedAtUtc,
                GetEpoch(exchange),
                GetSequence(exchange),
                payload.Length,
                exception.Message,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            await PublishAsync(
                exchange,
                QualityEventType.ParseRejection,
                receivedAtUtc,
                GetEpoch(exchange),
                GetSequence(exchange),
                payload.Length,
                exception.Message,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private long GetEpoch(Exchange exchange) =>
        exchange == Exchange.BinanceSpot ? _binanceSynchronizer.ConnectionEpoch : _coinbaseSynchronizer.ConnectionEpoch;

    private long? GetSequence(Exchange exchange) =>
        exchange == Exchange.BinanceSpot
            ? _binanceSynchronizer.ExportSnapshot().FinalSequence
            : _coinbaseSynchronizer.ExportSnapshot().FinalSequence;

    private ValueTask PublishAsync(
        Exchange exchange,
        QualityEventType type,
        DateTimeOffset occurredAtUtc,
        long epoch,
        long? sequence,
        int payloadBytes,
        string details,
        CancellationToken cancellationToken) =>
        _qualitySink.PublishAsync(
            new MarketDataQualityEvent(
                exchange,
                exchange == Exchange.BinanceSpot
                    ? _binanceSynchronizer.ExportSnapshot().Instrument
                    : _coinbaseSynchronizer.ExportSnapshot().Instrument,
                type,
                occurredAtUtc,
                epoch,
                sequence,
                payloadBytes,
                details),
            cancellationToken);
}
