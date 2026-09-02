namespace CryptoArbitrage.Service.Configuration;

public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    public List<MarketOptions> Markets { get; init; } = [];

    public BookProcessingOptions Book { get; init; } = new();

    public ReconnectOptions Reconnect { get; init; } = new();
}

public sealed class MarketOptions
{
    public string CanonicalSymbol { get; init; } = string.Empty;

    public string BaseAsset { get; init; } = string.Empty;

    public string QuoteAsset { get; init; } = string.Empty;

    public BinanceMarketOptions Binance { get; init; } = new();

    public CoinbaseMarketOptions Coinbase { get; init; } = new();
}

public sealed class BinanceMarketOptions
{
    public string Symbol { get; init; } = string.Empty;

    public Uri? WebSocketBaseUri { get; init; }

    public Uri? RestBaseUri { get; init; }

    public string DepthStreamInterval { get; init; } = string.Empty;

    public VenuePrecisionOptions Precision { get; init; } = new();
}

public sealed class CoinbaseMarketOptions
{
    public string ProductId { get; init; } = string.Empty;

    public Uri? WebSocketUri { get; init; }

    public VenuePrecisionOptions Precision { get; init; } = new();
}

public sealed class VenuePrecisionOptions
{
    public int PriceScale { get; init; }

    public long PriceTickUnits { get; init; }

    public int QuantityScale { get; init; }

    public long QuantityIncrementUnits { get; init; }
}

public sealed class BookProcessingOptions
{
    public int SnapshotDepth { get; init; }

    public int RetainedDepth { get; init; }

    public TimeSpan Freshness { get; init; }

    public int QueueCapacity { get; init; }
}

public sealed class ReconnectOptions
{
    public TimeSpan InitialDelay { get; init; }

    public TimeSpan MaximumDelay { get; init; }

    public double Multiplier { get; init; }

    public double JitterFraction { get; init; }

    public int MaximumAttempts { get; init; }

    public TimeSpan LivenessTimeout { get; init; }

    public int MaximumFrameBytes { get; init; }
}
