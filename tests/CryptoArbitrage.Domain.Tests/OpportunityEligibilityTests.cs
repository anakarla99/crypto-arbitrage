using CryptoArbitrage.Domain;
using Xunit;

namespace CryptoArbitrage.Domain.Tests;

public sealed class OpportunityEligibilityTests
{
    [Fact]
    public void AcceptsFreshValidBooksAtTheFreshnessBoundary()
    {
        var observed = DateTimeOffset.UtcNow;
        var buy = ValidBook(Exchange.BinanceSpot, new CanonicalInstrumentId("BTC", "USDT"), observed - TimeSpan.FromMilliseconds(250));
        var sell = ValidBook(Exchange.CoinbaseAdvancedTrade, new CanonicalInstrumentId("BTC", "USDT"), observed);

        Assert.Equal(OpportunityEligibility.Eligible, OpportunityEligibilityEvaluator.Evaluate(buy, sell, observed, TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void RejectsAStaleOrNonComparableBook()
    {
        var observed = DateTimeOffset.UtcNow;
        var stale = ValidBook(Exchange.BinanceSpot, new CanonicalInstrumentId("BTC", "USDT"), observed - TimeSpan.FromMilliseconds(251));
        var fresh = ValidBook(Exchange.CoinbaseAdvancedTrade, new CanonicalInstrumentId("BTC", "USDT"), observed);
        var usd = ValidBook(Exchange.CoinbaseAdvancedTrade, new CanonicalInstrumentId("BTC", "USD"), observed);

        Assert.Equal(OpportunityEligibility.BuyBookStale, OpportunityEligibilityEvaluator.Evaluate(stale, fresh, observed, TimeSpan.FromMilliseconds(250)));
        Assert.Equal(OpportunityEligibility.InstrumentMismatch, OpportunityEligibilityEvaluator.Evaluate(fresh, usd, observed, TimeSpan.FromMilliseconds(250)));
    }

    private static BookView ValidBook(Exchange exchange, CanonicalInstrumentId id, DateTimeOffset receivedAt) => new(
        exchange,
        id,
        BookStatus.Valid,
        BookInvalidationReason.None,
        receivedAt,
        1,
        1,
        100,
        new BookLevel(new FixedPoint(65_000_00000000), new FixedPoint(1_000)),
        new BookLevel(new FixedPoint(65_001_00000000), new FixedPoint(1_000)));
}
