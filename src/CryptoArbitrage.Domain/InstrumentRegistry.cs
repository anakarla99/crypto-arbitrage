namespace CryptoArbitrage.Domain;

public sealed class InstrumentRegistry
{
    private readonly IReadOnlyDictionary<CanonicalInstrumentId, InstrumentDefinition> _byCanonical;
    private readonly IReadOnlyDictionary<VenueKey, InstrumentDefinition> _byVenue;

    public InstrumentRegistry(IEnumerable<InstrumentDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var canonical = new Dictionary<CanonicalInstrumentId, InstrumentDefinition>();
        var venue = new Dictionary<VenueKey, InstrumentDefinition>();
        foreach (var definition in definitions)
        {
            if (!canonical.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException($"Duplicate canonical instrument '{definition.Id}'.", nameof(definitions));
            }

            foreach (var mapping in definition.Venues)
            {
                if (!venue.TryAdd(new VenueKey(mapping.Exchange, mapping.Symbol), definition))
                {
                    throw new ArgumentException($"Duplicate venue mapping '{mapping.Exchange}:{mapping.Symbol}'.", nameof(definitions));
                }
            }
        }

        if (canonical.Count == 0)
        {
            throw new ArgumentException("At least one instrument definition is required.", nameof(definitions));
        }

        _byCanonical = canonical;
        _byVenue = venue;
    }

    public InstrumentDefinition Resolve(Exchange exchange, string venueSymbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(venueSymbol);
        return _byVenue.TryGetValue(new VenueKey(exchange, venueSymbol), out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown venue instrument '{exchange}:{venueSymbol}'.");
    }

    public bool TryResolve(Exchange exchange, string venueSymbol, out InstrumentDefinition? definition) =>
        _byVenue.TryGetValue(new VenueKey(exchange, venueSymbol), out definition);

    public InstrumentDefinition Get(CanonicalInstrumentId id) =>
        _byCanonical.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown canonical instrument '{id}'.");

    public static bool AreDirectlyComparable(CanonicalInstrumentId left, CanonicalInstrumentId right) => left == right;

    private readonly record struct VenueKey(Exchange Exchange, string Symbol);
}
