using System.Text.Json;
using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Infrastructure.Transport;

public sealed class BinanceDepthSubscription : IWebSocketSubscription
{
    public BinanceDepthSubscription(string symbol, string interval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(interval);
        Messages =
        [
            JsonSerializer.Serialize(new
            {
                method = "SUBSCRIBE",
                @params = new[] { $"{symbol.ToLowerInvariant()}@depth@{interval}" },
                id = 1
            })
        ];
    }

    public IReadOnlyList<string> Messages { get; }
}

public sealed class CoinbaseLevel2Subscription : IWebSocketSubscription
{
    public CoinbaseLevel2Subscription(string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        Messages =
        [
            JsonSerializer.Serialize(new
            {
                type = "subscribe",
                product_ids = new[] { productId },
                channel = "level2"
            })
        ];
    }

    public IReadOnlyList<string> Messages { get; }
}
