namespace CryptoArbitrage.Domain;

public readonly record struct CanonicalInstrumentId
{
    public CanonicalInstrumentId(string baseAsset, string quoteAsset)
    {
        BaseAsset = ValidateAsset(baseAsset, nameof(baseAsset));
        QuoteAsset = ValidateAsset(quoteAsset, nameof(quoteAsset));
        Value = $"{BaseAsset}-{QuoteAsset}";
    }

    public string BaseAsset { get; }

    public string QuoteAsset { get; }

    public string Value { get; }

    public override string ToString() => Value;

    private static string ValidateAsset(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (!string.Equals(value, value.ToUpperInvariant(), StringComparison.Ordinal) ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("Asset codes must be uppercase ASCII letters or digits.", parameterName);
        }

        return value;
    }
}
