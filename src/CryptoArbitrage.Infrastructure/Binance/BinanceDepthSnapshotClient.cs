using System.Net.Http.Json;
using System.Text.Json;
using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Infrastructure.Binance;

public interface IBinanceDepthSnapshotClient
{
    Task<BinanceDepthSnapshot> GetSnapshotAsync(string symbol, int limit, CancellationToken cancellationToken);
}

public sealed class BinanceDepthSnapshotClient : IBinanceDepthSnapshotClient
{
    private readonly HttpClient _httpClient;
    private readonly VenueInstrument _venueInstrument;

    public BinanceDepthSnapshotClient(HttpClient httpClient, VenueInstrument venueInstrument)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _venueInstrument = venueInstrument ?? throw new ArgumentNullException(nameof(venueInstrument));
        if (_venueInstrument.Exchange != Exchange.BinanceSpot)
        {
            throw new ArgumentException("The venue instrument must belong to Binance Spot.", nameof(venueInstrument));
        }
    }

    public async Task<BinanceDepthSnapshot> GetSnapshotAsync(
        string symbol,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (!string.Equals(symbol, _venueInstrument.Symbol, StringComparison.Ordinal))
        {
            throw new ArgumentException("The requested symbol does not match the configured venue instrument.", nameof(symbol));
        }

        if (limit is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        using var response = await _httpClient.GetAsync(
            $"api/v3/depth?symbol={Uri.EscapeDataString(symbol)}&limit={limit}",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var lastUpdateId = RequiredInt64(root, "lastUpdateId");
        return new BinanceDepthSnapshot(
            lastUpdateId,
            ParseLevels(root, "bids"),
            ParseLevels(root, "asks"));
    }

    private IReadOnlyList<BookLevel> ParseLevels(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var levels) || levels.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"Binance snapshot requires an array '{propertyName}'.");
        }

        var parsed = new List<BookLevel>();
        foreach (var level in levels.EnumerateArray())
        {
            if (level.ValueKind != JsonValueKind.Array || level.GetArrayLength() != 2 ||
                level[0].ValueKind != JsonValueKind.String || level[1].ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"Binance snapshot level in '{propertyName}' must contain string price and quantity.");
            }

            parsed.Add(new BookLevel(
                _venueInstrument.ParsePrice(level[0].GetString()!),
                _venueInstrument.ParseQuantity(level[1].GetString()!)));
        }

        return parsed;
    }

    private static long RequiredInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var value) ||
            value < 0)
        {
            throw new FormatException($"Binance snapshot requires non-negative integer '{propertyName}'.");
        }

        return value;
    }
}
