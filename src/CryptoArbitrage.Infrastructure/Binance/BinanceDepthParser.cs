using System.Globalization;
using System.Text.Json;
using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Infrastructure.Binance;

public sealed class BinanceDepthParser
{
    private readonly CanonicalInstrumentId _instrument;
    private readonly VenueInstrument _venueInstrument;

    public BinanceDepthParser(CanonicalInstrumentId instrument, VenueInstrument venueInstrument)
    {
        ArgumentNullException.ThrowIfNull(venueInstrument);
        if (venueInstrument.Exchange != Exchange.BinanceSpot)
        {
            throw new ArgumentException("The venue instrument must belong to Binance Spot.", nameof(venueInstrument));
        }

        _instrument = instrument;
        _venueInstrument = venueInstrument;
    }

    public BinanceDepthUpdate Parse(
        ReadOnlyMemory<byte> payload,
        DateTimeOffset receivedAtUtc,
        long receivedAtStopwatchTicks)
    {
        if (receivedAtUtc == default || receivedAtUtc.Offset != TimeSpan.Zero || receivedAtStopwatchTicks < 0)
        {
            throw new ArgumentException("Receipt timestamps must be UTC and monotonic ticks non-negative.");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!string.Equals(RequiredString(root, "e"), "depthUpdate", StringComparison.Ordinal))
        {
            throw new FormatException("Binance payload is not a depthUpdate event.");
        }

        if (!string.Equals(RequiredString(root, "s"), _venueInstrument.Symbol, StringComparison.Ordinal))
        {
            throw new FormatException($"Unexpected Binance symbol '{RequiredString(root, "s")}'.");
        }

        var firstUpdateId = RequiredInt64(root, "U");
        var finalUpdateId = RequiredInt64(root, "u");
        if (firstUpdateId < 0 || finalUpdateId < firstUpdateId)
        {
            throw new FormatException("Binance update ID range is invalid.");
        }

        var eventTime = ParseUnixMilliseconds(RequiredInt64(root, "E"));
        var bids = ParseLevels(root, "b");
        var asks = ParseLevels(root, "a");
        return new BinanceDepthUpdate(
            firstUpdateId,
            finalUpdateId,
            bids,
            asks,
            receivedAtUtc,
            receivedAtStopwatchTicks,
            eventTime);
    }

    private IReadOnlyList<BookLevel> ParseLevels(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var levels) || levels.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"Binance depthUpdate requires an array '{propertyName}'.");
        }

        var parsed = new List<BookLevel>();
        foreach (var level in levels.EnumerateArray())
        {
            if (level.ValueKind != JsonValueKind.Array || level.GetArrayLength() != 2)
            {
                throw new FormatException($"Binance depth level in '{propertyName}' must contain price and quantity.");
            }

            if (level[0].ValueKind != JsonValueKind.String || level[1].ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"Binance depth level in '{propertyName}' must use string values.");
            }

            var price = _venueInstrument.ParsePrice(level[0].GetString()!);
            var quantity = _venueInstrument.ParseQuantity(level[1].GetString()!);
            parsed.Add(new BookLevel(price, quantity));
        }

        return parsed;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new FormatException($"Binance payload requires non-empty '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static long RequiredInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var value))
        {
            throw new FormatException($"Binance payload requires integer '{propertyName}'.");
        }

        return value;
    }

    private static DateTimeOffset ParseUnixMilliseconds(long milliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FormatException("Binance event time is outside the supported timestamp range.", exception);
        }
    }
}
