using CryptoArbitrage.Domain;
using Xunit;

namespace CryptoArbitrage.Domain.Tests;

public sealed class SpreadOpportunityTests
{
    [Fact]
    public void CalculatesTradableQuantityAndGrossSpread()
    {
        var observed = DateTimeOffset.UtcNow;
        var buy = ValidBook(Exchange.BinanceSpot, observed, 64_999_00000000, 65_000_00000000, 250_00000);
        var sell = ValidBook(Exchange.CoinbaseAdvancedTrade, observed, 65_026_00000000, 65_027_00000000, 100_00000);

        var result = SpreadOpportunityCalculator.Calculate(buy, sell, observed, TimeSpan.FromMilliseconds(250));

        Assert.NotNull(result);
        Assert.Equal(100_00000, result.Quantity.Units);
        Assert.Equal(26_00000000, result.GrossSpread.Units);
        Assert.Equal(4m, decimal.Round(result.GrossSpreadBps, 2));
    }

    [Fact]
    public void DoesNotCreateAnOpportunityForNonProfitablePrices()
    {
        var observed = DateTimeOffset.UtcNow;
        var buy = ValidBook(Exchange.BinanceSpot, observed, 64_999_00000000, 65_000_00000000, 100_00000);
        var sell = ValidBook(Exchange.CoinbaseAdvancedTrade, observed, 64_999_00000000, 65_000_00000000, 100_00000);

        Assert.Null(SpreadOpportunityCalculator.Calculate(buy, sell, observed, TimeSpan.FromMilliseconds(250)));
    }

    private static BookView ValidBook(
        Exchange exchange,
        DateTimeOffset receivedAt,
        long bestBid,
        long bestAsk,
        long quantity) => new(
        exchange,
        new CanonicalInstrumentId("BTC", "USDT"),
        BookStatus.Valid,
        BookInvalidationReason.None,
        receivedAt,
        1,
        1,
        100,
        new BookLevel(new FixedPoint(bestBid), new FixedPoint(quantity)),
        new BookLevel(new FixedPoint(bestAsk), new FixedPoint(quantity)));
}
