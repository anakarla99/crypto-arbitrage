using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Infrastructure.Coinbase;

public sealed class CoinbaseLevel2Change
{
    public CoinbaseLevel2Change(BookSide side, BookLevel level, DateTimeOffset? exchangeEventAtUtc = null)
    {
        if (exchangeEventAtUtc.HasValue && exchangeEventAtUtc.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Exchange event time must be UTC.", nameof(exchangeEventAtUtc));

        Side = side;
        Level = level;
        ExchangeEventAtUtc = exchangeEventAtUtc;
    }

    public BookSide Side { get; }
    public BookLevel Level { get; }
    public DateTimeOffset? ExchangeEventAtUtc { get; }
}

public sealed class CoinbaseLevel2Update
{
    public CoinbaseLevel2Update(long sequence, IReadOnlyList<CoinbaseLevel2Change> changes, DateTimeOffset receivedAtUtc, long receivedAtStopwatchTicks)
    {
        if (sequence < 0 || receivedAtUtc == default || receivedAtUtc.Offset != TimeSpan.Zero || receivedAtStopwatchTicks < 0)
            throw new ArgumentException("Level2 update metadata is invalid.");

        Sequence = sequence;
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
        ReceivedAtUtc = receivedAtUtc;
        ReceivedAtStopwatchTicks = receivedAtStopwatchTicks;
    }

    public long Sequence { get; }
    public IReadOnlyList<CoinbaseLevel2Change> Changes { get; }
    public DateTimeOffset ReceivedAtUtc { get; }
    public long ReceivedAtStopwatchTicks { get; }
}

public sealed class CoinbaseLevel2Snapshot
{
    public CoinbaseLevel2Snapshot(long sequence, IReadOnlyList<BookLevel> bids, IReadOnlyList<BookLevel> asks, DateTimeOffset receivedAtUtc, long receivedAtStopwatchTicks, DateTimeOffset? exchangeEventAtUtc = null)
    {
        if (sequence < 0 || receivedAtUtc == default || receivedAtUtc.Offset != TimeSpan.Zero || receivedAtStopwatchTicks < 0 ||
            (exchangeEventAtUtc.HasValue && exchangeEventAtUtc.Value.Offset != TimeSpan.Zero))
            throw new ArgumentException("Level2 snapshot metadata is invalid.");

        Sequence = sequence;
        Bids = bids ?? throw new ArgumentNullException(nameof(bids));
        Asks = asks ?? throw new ArgumentNullException(nameof(asks));
        ReceivedAtUtc = receivedAtUtc;
        ReceivedAtStopwatchTicks = receivedAtStopwatchTicks;
        ExchangeEventAtUtc = exchangeEventAtUtc;
    }

    public long Sequence { get; }
    public IReadOnlyList<BookLevel> Bids { get; }
    public IReadOnlyList<BookLevel> Asks { get; }
    public DateTimeOffset ReceivedAtUtc { get; }
    public long ReceivedAtStopwatchTicks { get; }
    public DateTimeOffset? ExchangeEventAtUtc { get; }
}

/// <summary>Maintains a Coinbase Advanced Trade level2 book after its stream snapshot.</summary>
public sealed class CoinbaseLevel2Synchronizer
{
    private readonly CanonicalInstrumentId _instrument;
    private readonly int _retainedDepth;
    private readonly SortedDictionary<long, long> _bids = new();
    private readonly SortedDictionary<long, long> _asks = new();
    private readonly List<CoinbaseLevel2Update> _buffered = [];
    private long? _lastSequence;
    private long _connectionEpoch;
    private DateTimeOffset? _lastAppliedReceivedAtUtc;
    private long? _lastAppliedStopwatchTicks;
    private DateTimeOffset? _sourceEventAtUtc;
    private BookStatus _status = BookStatus.Synchronizing;
    private BookInvalidationReason _reason = BookInvalidationReason.Reconnect;

    public CoinbaseLevel2Synchronizer(CanonicalInstrumentId instrument, int retainedDepth)
    {
        if (retainedDepth <= 0) throw new ArgumentOutOfRangeException(nameof(retainedDepth));
        _instrument = instrument;
        _retainedDepth = retainedDepth;
    }

    public BookStatus Status => _status;
    public BookInvalidationReason InvalidationReason => _reason;
    public long ConnectionEpoch => _connectionEpoch;

    public void BeginSynchronization() => BeginSynchronization(_connectionEpoch + 1);

    public void BeginSynchronization(long connectionEpoch)
    {
        if (connectionEpoch <= _connectionEpoch)
            throw new ArgumentOutOfRangeException(nameof(connectionEpoch), "Connection epoch must increase.");

        _bids.Clear(); _asks.Clear(); _buffered.Clear(); _lastSequence = null;
        _lastAppliedReceivedAtUtc = null; _lastAppliedStopwatchTicks = null; _sourceEventAtUtc = null;
        _connectionEpoch = connectionEpoch;
        _status = BookStatus.Synchronizing; _reason = BookInvalidationReason.Reconnect;
    }

    public void Buffer(CoinbaseLevel2Update update)
    {
        if (_status == BookStatus.Synchronizing) _buffered.Add(update);
        else ApplyLive(update);
    }

    public bool ApplySnapshot(CoinbaseLevel2Snapshot snapshot)
    {
        if (_status != BookStatus.Synchronizing) return false;
        if (_connectionEpoch == 0) _connectionEpoch = 1;

        _bids.Clear(); _asks.Clear();
        ApplyLevels(_bids, snapshot.Bids); ApplyLevels(_asks, snapshot.Asks);
        _lastSequence = snapshot.Sequence;
        _lastAppliedReceivedAtUtc = snapshot.ReceivedAtUtc;
        _lastAppliedStopwatchTicks = snapshot.ReceivedAtStopwatchTicks;
        _sourceEventAtUtc = snapshot.ExchangeEventAtUtc;

        foreach (var update in _buffered.Where(candidate => candidate.Sequence > snapshot.Sequence))
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

    public bool ApplyLive(CoinbaseLevel2Update update)
    {
        if (_status != BookStatus.Valid || _lastSequence is null) return false;
        return ApplySequenced(update);
    }

    public SynchronizedBookSnapshot ExportSnapshot() => new(
        Exchange.CoinbaseAdvancedTrade, _instrument, _status, _reason, _connectionEpoch,
        _status == BookStatus.Valid ? _lastAppliedReceivedAtUtc : null,
        _status == BookStatus.Valid ? _lastAppliedStopwatchTicks : null,
        _status == BookStatus.Valid ? _sourceEventAtUtc : null,
        _status == BookStatus.Valid ? _lastSequence : null,
        _retainedDepth,
        _status == BookStatus.Valid ? Levels(_bids, bid: true) : [],
        _status == BookStatus.Valid ? Levels(_asks, bid: false) : []);

    private bool ApplySequenced(CoinbaseLevel2Update update)
    {
        if (update.Sequence <= _lastSequence) return true;
        if (update.Sequence != _lastSequence + 1)
        {
            Invalidate(BookInvalidationReason.SequenceGap); return false;
        }

        foreach (var change in update.Changes)
            ApplyLevel(change.Side == BookSide.Bid ? _bids : _asks, change.Level);
        _lastSequence = update.Sequence;
        _lastAppliedReceivedAtUtc = update.ReceivedAtUtc;
        _lastAppliedStopwatchTicks = update.ReceivedAtStopwatchTicks;
        _sourceEventAtUtc = update.Changes.LastOrDefault()?.ExchangeEventAtUtc;

        if (HasValidBbo()) return true;
        Invalidate(BookInvalidationReason.SnapshotMismatch);
        return false;
    }

    private static void ApplyLevels(SortedDictionary<long, long> side, IReadOnlyList<BookLevel> levels)
    {
        foreach (var level in levels) ApplyLevel(side, level);
    }

    private static void ApplyLevel(SortedDictionary<long, long> side, BookLevel level)
    {
        if (level.Quantity.Units == 0) side.Remove(level.Price.Units);
        else side[level.Price.Units] = level.Quantity.Units;
    }

    private IReadOnlyList<BookLevel> Levels(SortedDictionary<long, long> side, bool bid) =>
        (bid ? side.Reverse() : side).Take(_retainedDepth)
            .Select(pair => new BookLevel(new FixedPoint(pair.Key), new FixedPoint(pair.Value)))
            .ToArray();

    private bool HasValidBbo() => _bids.Count > 0 && _asks.Count > 0 && _bids.Last().Key < _asks.First().Key;

    private void Invalidate(BookInvalidationReason reason)
    {
        _status = BookStatus.Invalid; _reason = reason; _bids.Clear(); _asks.Clear(); _buffered.Clear(); _lastSequence = null;
        _lastAppliedReceivedAtUtc = null; _lastAppliedStopwatchTicks = null; _sourceEventAtUtc = null;
    }
}
