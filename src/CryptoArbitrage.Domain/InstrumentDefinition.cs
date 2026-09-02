namespace CryptoArbitrage.Domain;

public sealed record VenuePrecision(int PriceScale, long PriceTickUnits, int QuantityScale, long QuantityIncrementUnits)
{
    public void Validate()
    {
        if (PriceScale is < 0 or > 12 || QuantityScale is < 0 or > 12 ||
            PriceTickUnits <= 0 || QuantityIncrementUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VenuePrecision), "Scales must be 0–12 and increments must be positive.");
        }
    }
}

public sealed record VenueInstrument
{
    public VenueInstrument(Exchange exchange, string symbol, VenuePrecision precision)
    {
        Exchange = exchange;
        Symbol = ValidateSymbol(symbol);
        ArgumentNullException.ThrowIfNull(precision);
        Precision = precision;
        Precision.Validate();
    }

    public Exchange Exchange { get; }
    public string Symbol { get; }
    public VenuePrecision Precision { get; }

    public FixedPoint ParsePrice(string value)
    {
        var price = FixedPoint.Parse(value, Precision.PriceScale, nameof(value));
        if (price.Units == 0 || price.Units % Precision.PriceTickUnits != 0)
        {
            throw new ArgumentException("Price must be positive and align to the venue price tick.", nameof(value));
        }

        return price;
    }

    public FixedPoint ParseQuantity(string value)
    {
        var quantity = FixedPoint.Parse(value, Precision.QuantityScale, nameof(value));
        if (quantity.Units != 0 && quantity.Units % Precision.QuantityIncrementUnits != 0)
        {
            throw new ArgumentException("Quantity must be zero or align to the venue quantity increment.", nameof(value));
        }

        return quantity;
    }

    private static string ValidateSymbol(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}

public sealed record InstrumentDefinition
{
    public InstrumentDefinition(CanonicalInstrumentId id, IReadOnlyCollection<VenueInstrument> venues)
    {
        Id = id;
        Venues = ValidateVenues(venues);
    }

    public CanonicalInstrumentId Id { get; }
    public IReadOnlyCollection<VenueInstrument> Venues { get; }

    private static IReadOnlyCollection<VenueInstrument> ValidateVenues(IReadOnlyCollection<VenueInstrument> venues)
    {
        ArgumentNullException.ThrowIfNull(venues);
        if (venues.Count == 0 || venues.GroupBy(venue => venue.Exchange).Any(group => group.Count() != 1))
        {
            throw new ArgumentException("An instrument requires one unique mapping per exchange.", nameof(venues));
        }

        return venues.ToArray();
    }
}
