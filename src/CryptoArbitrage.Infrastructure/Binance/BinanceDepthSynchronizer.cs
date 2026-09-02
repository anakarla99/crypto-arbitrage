using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Infrastructure.Binance;

public sealed class BinanceDepthUpdate
{
    public BinanceDepthUpdate(long firstUpdateId, long finalUpdateId, IReadOnlyList<BookLevel> bids, IReadOnlyList<BookLevel> asks, DateTimeOffset receivedAtUtc, long receivedAtStopwatchTicks, DateTimeOffset? exchangeEventAtUtc = null)
    {
        if (firstUpdateId < 0 || finalUpdateId < firstUpdateId || receivedAtUtc == default || receivedAtUtc.Offset != TimeSpan.Zero || receivedAtStopwatchTicks < 0 ||
            (exchangeEventAtUtc.HasValue && exchangeEventAtUtc.Value.Offset != TimeSpan.Zero))
            throw new ArgumentException("Depth update metadata is invalid.");
        FirstUpdateId = firstUpdateId; FinalUpdateId = finalUpdateId; Bids = bids ?? throw new ArgumentNullException(nameof(bids)); Asks = asks ?? throw new ArgumentNullException(nameof(asks)); ReceivedAtUtc = receivedAtUtc; ReceivedAtStopwatchTicks = receivedAtStopwatchTicks; ExchangeEventAtUtc = exchangeEventAtUtc;
    }
    public long FirstUpdateId { get; } public long FinalUpdateId { get; } public IReadOnlyList<BookLevel> Bids { get; } public IReadOnlyList<BookLevel> Asks { get; } public DateTimeOffset ReceivedAtUtc { get; } public long ReceivedAtStopwatchTicks { get; } public DateTimeOffset? ExchangeEventAtUtc { get; }
}

public sealed class BinanceDepthSnapshot
{
    public BinanceDepthSnapshot(long lastUpdateId, IReadOnlyList<BookLevel> bids, IReadOnlyList<BookLevel> asks)
    {
        if (lastUpdateId < 0) throw new ArgumentOutOfRangeException(nameof(lastUpdateId));
        LastUpdateId = lastUpdateId; Bids = bids ?? throw new ArgumentNullException(nameof(bids)); Asks = asks ?? throw new ArgumentNullException(nameof(asks));
    }
    public long LastUpdateId { get; } public IReadOnlyList<BookLevel> Bids { get; } public IReadOnlyList<BookLevel> Asks { get; }
}

public sealed class BinanceDepthSynchronizer
{
    private readonly CanonicalInstrumentId _instrument;
    private readonly int _retainedDepth;
    private readonly SortedDictionary<long, long> _bids = new();
    private readonly SortedDictionary<long, long> _asks = new();
    private readonly List<BinanceDepthUpdate> _buffered = [];
    private long? _lastUpdateId;
    private long _connectionEpoch;
    private DateTimeOffset? _lastAppliedReceivedAtUtc;
    private long? _lastAppliedStopwatchTicks;
    private DateTimeOffset? _sourceEventAtUtc;
    private BookStatus _status = BookStatus.Synchronizing;
    private BookInvalidationReason _reason = BookInvalidationReason.Reconnect;

    public BinanceDepthSynchronizer(CanonicalInstrumentId instrument, int retainedDepth)
    {
        if (retainedDepth <= 0) throw new ArgumentOutOfRangeException(nameof(retainedDepth));
        _instrument = instrument;
        _retainedDepth = retainedDepth;
    }

    public BookStatus Status => _status;
    public BookInvalidationReason InvalidationReason => _reason;
    public long ConnectionEpoch => _connectionEpoch;

    public void BeginSynchronization()
    {
        BeginSynchronization(_connectionEpoch + 1);
    }

    public void BeginSynchronization(long connectionEpoch)
    {
        if (connectionEpoch <= _connectionEpoch)
            throw new ArgumentOutOfRangeException(nameof(connectionEpoch), "Connection epoch must increase.");

        _bids.Clear(); _asks.Clear(); _buffered.Clear(); _lastUpdateId = null;
        _lastAppliedReceivedAtUtc = null; _lastAppliedStopwatchTicks = null; _sourceEventAtUtc = null;
        _connectionEpoch = connectionEpoch;
        _status = BookStatus.Synchronizing; _reason = BookInvalidationReason.Reconnect;
    }

    public void Buffer(BinanceDepthUpdate update)
    {
        if (_status != BookStatus.Synchronizing) { ApplyLive(update); return; }
        _buffered.Add(update);
    }

    public bool ApplySnapshot(BinanceDepthSnapshot snapshot)
    {
        if (_status != BookStatus.Synchronizing) return false;
        if (_connectionEpoch == 0) _connectionEpoch = 1;
        var pending = _buffered.Where(update => update.FinalUpdateId > snapshot.LastUpdateId).ToArray();
        if (pending.Length == 0 || pending[0].FirstUpdateId > snapshot.LastUpdateId + 1 || pending[0].FinalUpdateId < snapshot.LastUpdateId + 1)
        {
            Invalidate(BookInvalidationReason.SnapshotMismatch); return false;
        }

        _bids.Clear(); _asks.Clear();
        ApplyLevels(_bids, snapshot.Bids); ApplyLevels(_asks, snapshot.Asks);
        _lastUpdateId = snapshot.LastUpdateId;
        ApplyUpdate(pending[0]);
        foreach (var update in pending.Skip(1))
        {
            if (!ApplySequenced(update)) return false;
        }

        _buffered.Clear();
        if (!HasValidBbo())
        {
            Invalidate(BookInvalidationReason.SnapshotMismatch); return false;
        }

        _status = BookStatus.Valid; _reason = BookInvalidationReason.None;
        return true;
    }

    public bool ApplyLive(BinanceDepthUpdate update)
    {
        if (_status != BookStatus.Valid || _lastUpdateId is null) return false;
        return ApplySequenced(update);
    }

    public BookView GetView(DateTimeOffset receivedAtUtc, long stopwatchTicks) => new(
        Exchange.BinanceSpot, _instrument, _status, _reason, receivedAtUtc, stopwatchTicks, _lastUpdateId, _retainedDepth,
        _status == BookStatus.Valid ? Best(_bids, true) : null,
        _status == BookStatus.Valid ? Best(_asks, false) : null);

    public SynchronizedBookSnapshot ExportSnapshot() => new(
        Exchange.BinanceSpot, _instrument, _status, _reason, _connectionEpoch,
        _status == BookStatus.Valid ? _lastAppliedReceivedAtUtc : null,
        _status == BookStatus.Valid ? _lastAppliedStopwatchTicks : null,
        _status == BookStatus.Valid ? _sourceEventAtUtc : null,
        _status == BookStatus.Valid ? _lastUpdateId : null,
        _retainedDepth,
        _status == BookStatus.Valid ? Levels(_bids, bid: true) : [],
        _status == BookStatus.Valid ? Levels(_asks, bid: false) : []);

    private bool ApplySequenced(BinanceDepthUpdate update)
    {
        if (update.FinalUpdateId <= _lastUpdateId) return true;
        if (update.FirstUpdateId != _lastUpdateId + 1)
        {
            Invalidate(BookInvalidationReason.SequenceGap); return false;
        }

        ApplyUpdate(update);
        if (!HasValidBbo()) { Invalidate(BookInvalidationReason.SnapshotMismatch); return false; }
        return true;
    }

    private static void ApplyLevels(SortedDictionary<long, long> side, IReadOnlyList<BookLevel> levels)
    {
        foreach (var level in levels) { if (level.Quantity.Units == 0) side.Remove(level.Price.Units); else side[level.Price.Units] = level.Quantity.Units; }
    }

    private void ApplyUpdate(BinanceDepthUpdate update)
    {
        ApplyLevels(_bids, update.Bids); ApplyLevels(_asks, update.Asks);
        _lastUpdateId = update.FinalUpdateId;
        _lastAppliedReceivedAtUtc = update.ReceivedAtUtc;
        _lastAppliedStopwatchTicks = update.ReceivedAtStopwatchTicks;
        _sourceEventAtUtc = update.ExchangeEventAtUtc;
    }

    private IReadOnlyList<BookLevel> Levels(SortedDictionary<long, long> side, bool bid) =>
        (bid ? side.Reverse() : side).Take(_retainedDepth)
            .Select(pair => new BookLevel(new FixedPoint(pair.Key), new FixedPoint(pair.Value)))
            .ToArray();

    private bool HasValidBbo() => _bids.Count > 0 && _asks.Count > 0 && _bids.Last().Key < _asks.First().Key;
    private static BookLevel? Best(SortedDictionary<long, long> side, bool bid)
    {
        if (side.Count == 0) return null;
        var pair = bid ? side.Last() : side.First();
        return new BookLevel(new FixedPoint(pair.Key), new FixedPoint(pair.Value));
    }
    private void Invalidate(BookInvalidationReason reason)
    {
        _status = BookStatus.Invalid; _reason = reason; _bids.Clear(); _asks.Clear(); _buffered.Clear(); _lastUpdateId = null;
        _lastAppliedReceivedAtUtc = null; _lastAppliedStopwatchTicks = null; _sourceEventAtUtc = null;
    }
}
