using CryptoArbitrage.Domain;
using CryptoArbitrage.Infrastructure.Binance;

namespace CryptoArbitrage.Infrastructure.Transport;

public sealed class MarketDataOrchestrator
{
    private readonly BinanceDepthSynchronizer _binanceSynchronizer;
    private readonly IBinanceDepthSnapshotClient _binanceSnapshotClient;
    private readonly IMarketDataQualitySink _qualitySink;
    private readonly string _binanceSymbol;
    private readonly int _binanceSnapshotDepth;

    public MarketDataOrchestrator(
        BinanceDepthSynchronizer binanceSynchronizer,
        IBinanceDepthSnapshotClient binanceSnapshotClient,
        IMarketDataQualitySink qualitySink,
        string binanceSymbol,
        int binanceSnapshotDepth)
    {
        _binanceSynchronizer = binanceSynchronizer ?? throw new ArgumentNullException(nameof(binanceSynchronizer));
        _binanceSnapshotClient = binanceSnapshotClient ?? throw new ArgumentNullException(nameof(binanceSnapshotClient));
        _qualitySink = qualitySink ?? throw new ArgumentNullException(nameof(qualitySink));
        ArgumentException.ThrowIfNullOrWhiteSpace(binanceSymbol);
        if (binanceSnapshotDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binanceSnapshotDepth));
        }

        _binanceSymbol = binanceSymbol;
        _binanceSnapshotDepth = binanceSnapshotDepth;
    }

    public void BeginBinanceEpoch(long connectionEpoch) =>
        _binanceSynchronizer.BeginSynchronization(connectionEpoch);

    public async Task<bool> SynchronizeBinanceAsync(
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _binanceSnapshotClient.GetSnapshotAsync(
                _binanceSymbol,
                _binanceSnapshotDepth,
                cancellationToken).ConfigureAwait(false);
            if (_binanceSynchronizer.ApplySnapshot(snapshot))
            {
                return true;
            }

            await PublishQualityAsync(
                QualityEventType.SnapshotMismatch,
                occurredAtUtc,
                "Binance REST snapshot could not bridge buffered WebSocket updates.",
                cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            await PublishQualityAsync(
                QualityEventType.SnapshotMismatch,
                occurredAtUtc,
                $"Binance REST snapshot request failed: {exception.Message}",
                cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (FormatException exception)
        {
            await PublishQualityAsync(
                QualityEventType.SnapshotMismatch,
                occurredAtUtc,
                $"Binance REST snapshot was invalid: {exception.Message}",
                cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private ValueTask PublishQualityAsync(
        QualityEventType type,
        DateTimeOffset occurredAtUtc,
        string details,
        CancellationToken cancellationToken) =>
        _qualitySink.PublishAsync(
            new MarketDataQualityEvent(
                Exchange.BinanceSpot,
                _binanceSynchronizer.ExportSnapshot().Instrument,
                type,
                occurredAtUtc,
                _binanceSynchronizer.ConnectionEpoch,
                _binanceSynchronizer.ExportSnapshot().FinalSequence,
                0,
                details),
            cancellationToken);
}
