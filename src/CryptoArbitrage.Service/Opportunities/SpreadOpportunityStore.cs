using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Service.Opportunities;

public interface ISpreadOpportunityStore
{
    void Save(SpreadOpportunity opportunity);

    SpreadOpportunity? GetLatest(CanonicalInstrumentId instrument);
}

public sealed class InMemorySpreadOpportunityStore : ISpreadOpportunityStore
{
    private readonly object sync = new();
    private readonly Dictionary<CanonicalInstrumentId, SpreadOpportunity> latest = [];

    public void Save(SpreadOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(opportunity);

        lock (sync)
        {
            latest[opportunity.Instrument] = opportunity;
        }
    }

    public SpreadOpportunity? GetLatest(CanonicalInstrumentId instrument)
    {
        lock (sync)
        {
            return latest.GetValueOrDefault(instrument);
        }
    }
}
