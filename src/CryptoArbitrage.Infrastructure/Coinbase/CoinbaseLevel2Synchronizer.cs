using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Infrastructure.Coinbase;

public sealed class CoinbaseLevel2Update
{
    public CoinbaseLevel2Update(
        Exchange exchange,
        CanonicalInstrumentId instrument,
        DateTimeOffset receivedAtUtc,
        long receivedAtStopwatchTicks,
        DateTimeOffset exchangeEventTimeUtc,
        long sequenceNumber,
        BookUpdateKind kind,
        IReadOnlyList<BookLevel> bids,
        IReadOnlyList<BookLevel> asks)
    {
        if (exchange != Exchange.CoinbaseAdvancedTrade ||
            receivedAtUtc == default || receivedAtUtc.Offset != TimeSpan.Zero ||
            receivedAtStopwatchTicks < 0 || exchangeEventTimeUtc.Offset != TimeSpan.Zero ||
            sequenceNumber < 0)
        {
            throw new ArgumentException("Coinbase Level 2 update metadata is invalid.");
        }

        Exchange = exchange;
        Instrument = instrument;
        ReceivedAtUtc = receivedAtUtc;
        ReceivedAtStopwatchTicks = receivedAtStopwatchTicks;
        ExchangeEventTimeUtc = exchangeEventTimeUtc;
        SequenceNumber = sequenceNumber;
        Kind = kind;
        Bids = bids?.ToArray() ?? throw new ArgumentNullException(nameof(bids));
        Asks = asks?.ToArray() ?? throw new ArgumentNullException(nameof(asks));
    }

    public Exchange Exchange { get; }
    public CanonicalInstrumentId Instrument { get; }
    public DateTimeOffset ReceivedAtUtc { get; }
    public long ReceivedAtStopwatchTicks { get; }
    public DateTimeOffset ExchangeEventTimeUtc { get; }
    public long SequenceNumber { get; }
    public BookUpdateKind Kind { get; }
    public IReadOnlyList<BookLevel> Bids { get; }
    public IReadOnlyList<BookLevel> Asks { get; }
}

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
        {
            throw new ArgumentOutOfRangeException(nameof(connectionEpoch), "Connection epoch must increase.");
        }

        _bids.Clear();
        _asks.Clear();
        _buffered.Clear();
        _lastSequence = null;
        _lastAppliedReceivedAtUtc = null;
        _lastAppliedStopwatchTicks = null;
        _sourceEventAtUtc = null;
        _connectionEpoch = connectionEpoch;
        _status = BookStatus.Synchronizing;
        _reason = BookInvalidationReason.Reconnect;
    }

    public void Buffer(CoinbaseLevel2Update update)
    {
        ValidateUpdate(update);
        if (_status == BookStatus.Synchronizing)
        {
            _buffered.Add(update);
            return;
        }

        ApplyLive(update);
    }

    public bool ApplySnapshot(CoinbaseLevel2Update snapshot)
    {
        ValidateUpdate(snapshot);
        if (_status != BookStatus.Synchronizing || snapshot.Kind != BookUpdateKind.Snapshot)
        {
            return false;
        }

        if (_connectionEpoch == 0)
        {
            _connectionEpoch = 1;
        }

        _bids.Clear();
        _asks.Clear();
        ApplyLevels(_bids, snapshot.Bids);
        ApplyLevels(_asks, snapshot.Asks);
        _lastSequence = snapshot.SequenceNumber;
        ApplyMetadata(snapshot);

        foreach (var update in _buffered
                     .Where(update => update.SequenceNumber > snapshot.SequenceNumber)
                     .OrderBy(update => update.SequenceNumber))
        {
            if (!ApplySequenced(update))
            {
                return false;
            }
        }

        _buffered.Clear();
        if (!HasValidBbo())
        {
            Invalidate(BookInvalidationReason.SnapshotMismatch);
            return false;
        }

        _status = BookStatus.Valid;
        _reason = BookInvalidationReason.None;
        return true;
    }

    public bool ApplyLive(CoinbaseLevel2Update update)
    {
        ValidateUpdate(update);
        if (_status != BookStatus.Valid || _lastSequence is null || update.Kind != BookUpdateKind.Delta)
        {
            return false;
        }

        return ApplySequenced(update);
    }

    public BookView GetView(DateTimeOffset receivedAtUtc, long stopwatchTicks) => new(
        Exchange.CoinbaseAdvancedTrade,
        _instrument,
        _status,
        _reason,
        receivedAtUtc,
        stopwatchTicks,
        _lastSequence,
        _retainedDepth,
        _status == BookStatus.Valid ? Best(_bids, bid: true) : null,
        _status == BookStatus.Valid ? Best(_asks, bid: false) : null);

    public SynchronizedBookSnapshot ExportSnapshot() => new(
        Exchange.CoinbaseAdvancedTrade,
        _instrument,
        _status,
        _reason,
        _connectionEpoch,
        _status == BookStatus.Valid ? _lastAppliedReceivedAtUtc : null,
        _status == BookStatus.Valid ? _lastAppliedStopwatchTicks : null,
        _status == BookStatus.Valid ? _sourceEventAtUtc : null,
        _status == BookStatus.Valid ? _lastSequence : null,
        _retainedDepth,
        _status == BookStatus.Valid ? Levels(_bids, bid: true) : [],
        _status == BookStatus.Valid ? Levels(_asks, bid: false) : []);

    private bool ApplySequenced(CoinbaseLevel2Update update)
    {
        if (update.SequenceNumber <= _lastSequence)
        {
            return true;
        }

        if (update.SequenceNumber != _lastSequence + 1)
        {
            Invalidate(BookInvalidationReason.SequenceGap);
            return false;
        }

        ApplyLevels(_bids, update.Bids);
        ApplyLevels(_asks, update.Asks);
        _lastSequence = update.SequenceNumber;
        ApplyMetadata(update);
        if (!HasValidBbo())
        {
            Invalidate(BookInvalidationReason.SnapshotMismatch);
            return false;
        }

        return true;
    }

    private void ApplyMetadata(CoinbaseLevel2Update update)
    {
        _lastAppliedReceivedAtUtc = update.ReceivedAtUtc;
        _lastAppliedStopwatchTicks = update.ReceivedAtStopwatchTicks;
        _sourceEventAtUtc = update.ExchangeEventTimeUtc;
    }

    private static void ApplyLevels(SortedDictionary<long, long> side, IReadOnlyList<BookLevel> levels)
    {
        foreach (var level in levels)
        {
            if (level.Quantity.Units == 0)
            {
                side.Remove(level.Price.Units);
            }
            else
            {
                side[level.Price.Units] = level.Quantity.Units;
            }
        }
    }

    private IReadOnlyList<BookLevel> Levels(SortedDictionary<long, long> side, bool bid) =>
        (bid ? side.Reverse() : side)
            .Take(_retainedDepth)
            .Select(pair => new BookLevel(new FixedPoint(pair.Key), new FixedPoint(pair.Value)))
            .ToArray();

    private bool HasValidBbo() =>
        _bids.Count > 0 && _asks.Count > 0 && _bids.Last().Key < _asks.First().Key;

    private static BookLevel? Best(SortedDictionary<long, long> side, bool bid)
    {
        if (side.Count == 0)
        {
            return null;
        }

        var pair = bid ? side.Last() : side.First();
        return new BookLevel(new FixedPoint(pair.Key), new FixedPoint(pair.Value));
    }

    private void Invalidate(BookInvalidationReason reason)
    {
        _status = BookStatus.Invalid;
        _reason = reason;
        _bids.Clear();
        _asks.Clear();
        _buffered.Clear();
        _lastSequence = null;
        _lastAppliedReceivedAtUtc = null;
        _lastAppliedStopwatchTicks = null;
        _sourceEventAtUtc = null;
    }

    private void ValidateUpdate(CoinbaseLevel2Update update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Exchange != Exchange.CoinbaseAdvancedTrade || update.Instrument != _instrument)
        {
            throw new ArgumentException("Coinbase update does not match this synchronizer.", nameof(update));
        }
    }
}
