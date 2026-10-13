namespace CryptoArbitrage.Domain;

public sealed class SpreadOpportunity
{
    public SpreadOpportunity(
        CanonicalInstrumentId instrument,
        Exchange buyExchange,
        Exchange sellExchange,
        DateTimeOffset observedAtUtc,
        FixedPoint buyAsk,
        FixedPoint sellBid,
        FixedPoint quantity,
        FixedPoint grossSpread,
        decimal grossSpreadBps)
    {
        if (observedAtUtc == default || observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Observation time must be UTC.", nameof(observedAtUtc));
        }

        if (buyAsk.Units <= 0 || sellBid.Units <= buyAsk.Units || quantity.Units <= 0 || grossSpread.Units <= 0 ||
            grossSpreadBps <= 0)
        {
            throw new ArgumentException("A spread opportunity must be profitable and have positive quantity.");
        }

        Instrument = instrument;
        BuyExchange = buyExchange;
        SellExchange = sellExchange;
        ObservedAtUtc = observedAtUtc;
        BuyAsk = buyAsk;
        SellBid = sellBid;
        Quantity = quantity;
        GrossSpread = grossSpread;
        GrossSpreadBps = grossSpreadBps;
    }

    public CanonicalInstrumentId Instrument { get; }
    public Exchange BuyExchange { get; }
    public Exchange SellExchange { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public FixedPoint BuyAsk { get; }
    public FixedPoint SellBid { get; }
    public FixedPoint Quantity { get; }
    public FixedPoint GrossSpread { get; }
    public decimal GrossSpreadBps { get; }
}

public static class SpreadOpportunityCalculator
{
    public static SpreadOpportunity? Calculate(
        BookView buyBook,
        BookView sellBook,
        DateTimeOffset observedAtUtc,
        TimeSpan freshness)
    {
        var eligibility = OpportunityEligibilityEvaluator.Evaluate(buyBook, sellBook, observedAtUtc, freshness);
        if (eligibility != OpportunityEligibility.Eligible ||
            buyBook.BestAsk is not { } buyAsk ||
            sellBook.BestBid is not { } sellBid ||
            sellBid.Price.Units <= buyAsk.Price.Units)
        {
            return null;
        }

        var quantity = new FixedPoint(Math.Min(buyAsk.Quantity.Units, sellBid.Quantity.Units));
        var grossSpread = new FixedPoint(sellBid.Price.Units - buyAsk.Price.Units);
        var grossSpreadBps = decimal.Divide(grossSpread.Units * 10_000m, buyAsk.Price.Units);

        return new SpreadOpportunity(
            buyBook.Instrument,
            buyBook.Exchange,
            sellBook.Exchange,
            observedAtUtc,
            buyAsk.Price,
            sellBid.Price,
            quantity,
            grossSpread,
            grossSpreadBps);
    }
}
