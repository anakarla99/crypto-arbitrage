using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Service.Configuration;

public static class InstrumentRegistryFactory
{
    public static InstrumentRegistry Create(MarketDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var definitions = options.Markets.Select(market =>
        {
            var id = new CanonicalInstrumentId(market.BaseAsset, market.QuoteAsset);
            return new InstrumentDefinition(id,
            [
                new VenueInstrument(
                    Exchange.BinanceSpot,
                    market.Binance.Symbol,
                    ToDomainPrecision(market.Binance.Precision)),
                new VenueInstrument(
                    Exchange.CoinbaseAdvancedTrade,
                    market.Coinbase.ProductId,
                    ToDomainPrecision(market.Coinbase.Precision))
            ]);
        });

        return new InstrumentRegistry(definitions);
    }

    private static VenuePrecision ToDomainPrecision(VenuePrecisionOptions precision) => new(
        precision.PriceScale,
        precision.PriceTickUnits,
        precision.QuantityScale,
        precision.QuantityIncrementUnits);
}
