using CryptoArbitrage.Domain;
using Xunit;

namespace CryptoArbitrage.Domain.Tests;

public sealed class FixedPointTests
{
    [Fact]
    public void PreservesExactTickScaledValue()
    {
        var precision = new VenuePrecision(8, 1_000_000, 8, 1_000);
        var venue = new VenueInstrument(Exchange.BinanceSpot, "BTCUSDT", precision);

        Assert.Equal("65000.01000000", venue.ParsePrice("65000.01").ToDisplayString(8));
        Assert.Equal("0.00001000", venue.ParseQuantity("0.00001000").ToDisplayString(8));
    }

    [Fact]
    public void RejectsExcessPrecisionAndMisalignedVenueIncrements()
    {
        var venue = new VenueInstrument(Exchange.BinanceSpot, "BTCUSDT", new VenuePrecision(8, 1_000_000, 8, 1_000));

        Assert.Throws<ArgumentException>(() => venue.ParsePrice("65000.001"));
        Assert.Throws<ArgumentException>(() => venue.ParseQuantity("0.000001"));
        Assert.Equal(0, venue.ParseQuantity("0").Units);
    }
}
