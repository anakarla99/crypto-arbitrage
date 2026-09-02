namespace CryptoArbitrage.Domain;

public enum BookUpdateKind { Snapshot, Delta }

public enum BookSide { Bid, Ask }

public enum BookStatus { Synchronizing, Valid, Stale, Invalid }

public enum BookInvalidationReason { None, Disconnected, Reconnect, SequenceGap, QueueOverflow, SnapshotMismatch, MalformedMessage, LivenessTimeout }

public enum OpportunityEligibility { Eligible, InstrumentMismatch, BuyBookInvalid, SellBookInvalid, BuyBookStale, SellBookStale, MissingBid, MissingAsk, CrossedBook }

public readonly record struct BookSequenceRange
{
    public BookSequenceRange(long first, long final)
    {
        if (first < 0 || final < first)
        {
            throw new ArgumentOutOfRangeException(nameof(first), "Sequence range must be non-negative and ordered.");
        }

        First = first;
        Final = final;
    }

    public long First { get; }
    public long Final { get; }
}

public readonly record struct BookLevel
{
    public BookLevel(FixedPoint price, FixedPoint quantity)
    {
        if (price.Units <= 0 || quantity.Units < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive and quantity cannot be negative.");
        }

        Price = price;
        Quantity = quantity;
    }

    public FixedPoint Price { get; }
    public FixedPoint Quantity { get; }
}

public sealed class BookDelta
{
    public BookDelta(
        Exchange exchange,
        CanonicalInstrumentId instrument,
        DateTimeOffset receivedAtUtc,
        long receivedAtStopwatchTicks,
        DateTimeOffset? exchangeEventTimeUtc,
        BookSequenceRange? sequence,
        BookUpdateKind kind,
        IEnumerable<BookLevel> bids,
        IEnumerable<BookLevel> asks)
    {
        if (receivedAtUtc == default || receivedAtUtc.Offset != TimeSpan.Zero || receivedAtStopwatchTicks < 0 ||
            (exchangeEventTimeUtc.HasValue && exchangeEventTimeUtc.Value.Offset != TimeSpan.Zero))
        {
            throw new ArgumentException("Receipt timestamps must be UTC and monotonic ticks non-negative.", nameof(receivedAtUtc));
        }

        Exchange = exchange;
        Instrument = instrument;
        ReceivedAtUtc = receivedAtUtc;
        ReceivedAtStopwatchTicks = receivedAtStopwatchTicks;
        ExchangeEventTimeUtc = exchangeEventTimeUtc;
        Sequence = sequence;
        Kind = kind;
        Bids = bids?.ToArray() ?? throw new ArgumentNullException(nameof(bids));
        Asks = asks?.ToArray() ?? throw new ArgumentNullException(nameof(asks));
    }

    public Exchange Exchange { get; }
    public CanonicalInstrumentId Instrument { get; }
    public DateTimeOffset ReceivedAtUtc { get; }
    public long ReceivedAtStopwatchTicks { get; }
    public DateTimeOffset? ExchangeEventTimeUtc { get; }
    public BookSequenceRange? Sequence { get; }
    public BookUpdateKind Kind { get; }
    public IReadOnlyList<BookLevel> Bids { get; }
    public IReadOnlyList<BookLevel> Asks { get; }
}

public sealed class BookView
{
    public BookView(
        Exchange exchange,
        CanonicalInstrumentId instrument,
        BookStatus status,
        BookInvalidationReason invalidationReason,
        DateTimeOffset receivedAtUtc,
        long receivedAtStopwatchTicks,
        long? finalSequence,
        int retainedDepth,
        BookLevel? bestBid,
        BookLevel? bestAsk)
    {
        if (receivedAtUtc == default || receivedAtUtc.Offset != TimeSpan.Zero || receivedAtStopwatchTicks < 0 || retainedDepth < 0 ||
            (finalSequence.HasValue && finalSequence.Value < 0))
        {
            throw new ArgumentException("Book view metadata is invalid.");
        }

        if (status == BookStatus.Valid && (bestBid is null || bestAsk is null || bestBid.Value.Price.Units >= bestAsk.Value.Price.Units))
        {
            throw new ArgumentException("A valid book requires a non-crossed best bid and ask.");
        }

        if (status != BookStatus.Valid && (bestBid is not null || bestAsk is not null))
        {
            throw new ArgumentException("Only a valid book may expose best bid and ask.");
        }

        Exchange = exchange;
        Instrument = instrument;
        Status = status;
        InvalidationReason = invalidationReason;
        ReceivedAtUtc = receivedAtUtc;
        ReceivedAtStopwatchTicks = receivedAtStopwatchTicks;
        FinalSequence = finalSequence;
        RetainedDepth = retainedDepth;
        BestBid = bestBid;
        BestAsk = bestAsk;
    }

    public Exchange Exchange { get; }
    public CanonicalInstrumentId Instrument { get; }
    public BookStatus Status { get; }
    public BookInvalidationReason InvalidationReason { get; }
    public DateTimeOffset ReceivedAtUtc { get; }
    public long ReceivedAtStopwatchTicks { get; }
    public long? FinalSequence { get; }
    public int RetainedDepth { get; }
    public BookLevel? BestBid { get; }
    public BookLevel? BestAsk { get; }
}

public static class OpportunityEligibilityEvaluator
{
    public static OpportunityEligibility Evaluate(BookView buyBook, BookView sellBook, DateTimeOffset observedAtUtc, TimeSpan freshness)
    {
        ArgumentNullException.ThrowIfNull(buyBook);
        ArgumentNullException.ThrowIfNull(sellBook);

        if (!InstrumentRegistry.AreDirectlyComparable(buyBook.Instrument, sellBook.Instrument)) return OpportunityEligibility.InstrumentMismatch;
        if (buyBook.Status == BookStatus.Stale) return OpportunityEligibility.BuyBookStale;
        if (sellBook.Status == BookStatus.Stale) return OpportunityEligibility.SellBookStale;
        if (buyBook.Status != BookStatus.Valid) return OpportunityEligibility.BuyBookInvalid;
        if (sellBook.Status != BookStatus.Valid) return OpportunityEligibility.SellBookInvalid;
        if (observedAtUtc - buyBook.ReceivedAtUtc > freshness) return OpportunityEligibility.BuyBookStale;
        if (observedAtUtc - sellBook.ReceivedAtUtc > freshness) return OpportunityEligibility.SellBookStale;
        if (buyBook.BestAsk is null || sellBook.BestBid is null) return OpportunityEligibility.MissingAsk;
        if (buyBook.BestBid!.Value.Price.Units >= buyBook.BestAsk.Value.Price.Units || sellBook.BestBid.Value.Price.Units >= sellBook.BestAsk!.Value.Price.Units) return OpportunityEligibility.CrossedBook;
        return OpportunityEligibility.Eligible;
    }
}
