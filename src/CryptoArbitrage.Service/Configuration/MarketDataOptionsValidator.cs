namespace CryptoArbitrage.Service.Configuration;

using Microsoft.Extensions.Options;

public sealed class MarketDataOptionsValidator : IValidateOptions<MarketDataOptions>
{
    private static readonly HashSet<int> SupportedSnapshotDepths = [5, 10, 20, 50, 100, 500, 1000, 5000];

    public ValidateOptionsResult Validate(string? name, MarketDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateMarkets(options.Markets, failures);
        ValidateBook(options.Book, failures);
        ValidateReconnect(options.Reconnect, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateMarkets(List<MarketOptions> markets, List<string> failures)
    {
        if (markets.Count != 1)
        {
            failures.Add("MarketData:Markets must contain exactly one market during the Phase 2 MVP.");
            return;
        }

        var market = markets[0];
        ValidateUppercaseIdentifier(market.CanonicalSymbol, "MarketData:Markets:0:CanonicalSymbol", failures);
        ValidateUppercaseIdentifier(market.BaseAsset, "MarketData:Markets:0:BaseAsset", failures);
        ValidateUppercaseIdentifier(market.QuoteAsset, "MarketData:Markets:0:QuoteAsset", failures);

        var expectedCanonical = $"{market.BaseAsset}-{market.QuoteAsset}";
        if (!string.Equals(market.CanonicalSymbol, expectedCanonical, StringComparison.Ordinal))
        {
            failures.Add($"MarketData:Markets:0:CanonicalSymbol must equal '{expectedCanonical}'.");
        }

        ValidateUppercaseIdentifier(market.Binance.Symbol, "MarketData:Markets:0:Binance:Symbol", failures);
        ValidateUppercaseIdentifier(market.Coinbase.ProductId, "MarketData:Markets:0:Coinbase:ProductId", failures);

        if (!string.Equals(market.Binance.Symbol, $"{market.BaseAsset}{market.QuoteAsset}", StringComparison.Ordinal))
        {
            failures.Add("MarketData:Markets:0:Binance:Symbol must match the configured base and quote assets.");
        }

        if (!string.Equals(market.Coinbase.ProductId, expectedCanonical, StringComparison.Ordinal))
        {
            failures.Add("MarketData:Markets:0:Coinbase:ProductId must match the configured canonical symbol.");
        }

        ValidateUri(market.Binance.WebSocketBaseUri, "wss", "MarketData:Markets:0:Binance:WebSocketBaseUri", failures);
        ValidateUri(market.Binance.RestBaseUri, "https", "MarketData:Markets:0:Binance:RestBaseUri", failures);
        ValidateUri(market.Coinbase.WebSocketUri, "wss", "MarketData:Markets:0:Coinbase:WebSocketUri", failures);

        if (!string.Equals(market.Binance.DepthStreamInterval, "100ms", StringComparison.Ordinal))
        {
            failures.Add("MarketData:Markets:0:Binance:DepthStreamInterval must be '100ms' for the Phase 2 baseline.");
        }

        ValidatePrecision(market.Binance.Precision, "MarketData:Markets:0:Binance:Precision", failures);
        ValidatePrecision(market.Coinbase.Precision, "MarketData:Markets:0:Coinbase:Precision", failures);
    }

    private static void ValidateBook(BookProcessingOptions book, List<string> failures)
    {
        if (!SupportedSnapshotDepths.Contains(book.SnapshotDepth))
        {
            failures.Add("MarketData:Book:SnapshotDepth must be a supported Binance depth limit.");
        }

        if (book.RetainedDepth <= 0 || book.RetainedDepth > book.SnapshotDepth)
        {
            failures.Add("MarketData:Book:RetainedDepth must be positive and no greater than SnapshotDepth.");
        }

        if (book.Freshness < TimeSpan.FromMilliseconds(10) || book.Freshness > TimeSpan.FromSeconds(5))
        {
            failures.Add("MarketData:Book:Freshness must be between 10 ms and 5 s.");
        }

        if (book.QueueCapacity is < 32 or > 65536)
        {
            failures.Add("MarketData:Book:QueueCapacity must be between 32 and 65536.");
        }
    }

    private static void ValidatePrecision(VenuePrecisionOptions precision, string path, List<string> failures)
    {
        if (precision.PriceScale is < 0 or > 12 || precision.QuantityScale is < 0 or > 12 ||
            precision.PriceTickUnits <= 0 || precision.QuantityIncrementUnits <= 0)
        {
            failures.Add($"{path} scales must be 0–12 and increments must be positive.");
        }
    }

    private static void ValidateReconnect(ReconnectOptions reconnect, List<string> failures)
    {
        if (reconnect.InitialDelay <= TimeSpan.Zero || reconnect.MaximumDelay < reconnect.InitialDelay)
        {
            failures.Add("MarketData:Reconnect delays must be positive and MaximumDelay must be at least InitialDelay.");
        }

        if (reconnect.Multiplier < 1.1 || reconnect.Multiplier > 3.0)
        {
            failures.Add("MarketData:Reconnect:Multiplier must be between 1.1 and 3.0.");
        }

        if (reconnect.JitterFraction is < 0 or > 0.5)
        {
            failures.Add("MarketData:Reconnect:JitterFraction must be between 0 and 0.5.");
        }

        if (reconnect.MaximumAttempts is < 0 or > 100)
        {
            failures.Add("MarketData:Reconnect:MaximumAttempts must be between 0 and 100; 0 means unlimited.");
        }

        if (reconnect.LivenessTimeout < TimeSpan.FromSeconds(1) || reconnect.LivenessTimeout > TimeSpan.FromMinutes(5))
        {
            failures.Add("MarketData:Reconnect:LivenessTimeout must be between 1 s and 5 min.");
        }

        if (reconnect.MaximumFrameBytes is < 1 or > 1_048_576)
        {
            failures.Add("MarketData:Reconnect:MaximumFrameBytes must be between 1 and 1048576.");
        }
    }

    private static void ValidateUppercaseIdentifier(string value, string path, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.ToUpperInvariant(), StringComparison.Ordinal))
        {
            failures.Add($"{path} must be a non-empty uppercase identifier.");
        }
    }

    private static void ValidateUri(Uri? uri, string scheme, string path, List<string> failures)
    {
        if (uri is null || !uri.IsAbsoluteUri || !string.Equals(uri.Scheme, scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            failures.Add($"{path} must be an absolute {scheme} URI without user info, query, or fragment.");
        }
    }
}
