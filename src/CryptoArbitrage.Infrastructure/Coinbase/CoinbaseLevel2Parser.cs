using System.Globalization;
using System.Text.Json;
using CryptoArbitrage.Domain;

namespace CryptoArbitrage.Infrastructure.Coinbase;

public sealed class CoinbaseLevel2Parser
{
    private readonly CanonicalInstrumentId _instrument;
    private readonly VenueInstrument _venueInstrument;

    public CoinbaseLevel2Parser(CanonicalInstrumentId instrument, VenueInstrument venueInstrument)
    {
        ArgumentNullException.ThrowIfNull(venueInstrument);
        if (venueInstrument.Exchange != Exchange.CoinbaseAdvancedTrade)
        {
            throw new ArgumentException("The venue instrument must belong to Coinbase Advanced Trade.", nameof(venueInstrument));
        }

        _instrument = instrument;
        _venueInstrument = venueInstrument;
    }

    public IReadOnlyList<BookDelta> Parse(
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
        if (!root.TryGetProperty("channel", out var channel) ||
            !string.Equals(channel.GetString(), "l2_data", StringComparison.Ordinal))
        {
            throw new FormatException("Coinbase payload is not an l2_data message.");
        }

        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Coinbase l2_data payload must contain an events array.");
        }

        var deltas = new List<BookDelta>();
        foreach (var @event in events.EnumerateArray())
        {
            var type = RequiredString(@event, "type");
            var productId = RequiredString(@event, "product_id");
            if (!string.Equals(productId, _venueInstrument.Symbol, StringComparison.Ordinal))
            {
                throw new FormatException($"Unexpected Coinbase product '{productId}'.");
            }

            var eventTime = ParseUtcTimestamp(RequiredString(@event, "event_time"));
            if (!@event.TryGetProperty("updates", out var updates) || updates.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Coinbase l2_data event must contain an updates array.");
            }

            var bids = new List<BookLevel>();
            var asks = new List<BookLevel>();
            foreach (var update in updates.EnumerateArray())
            {
                var side = RequiredString(update, "side");
                var price = _venueInstrument.ParsePrice(RequiredString(update, "price_level"));
                var quantity = _venueInstrument.ParseQuantity(RequiredString(update, "new_quantity"));
                var level = new BookLevel(price, quantity);
                switch (side)
                {
                    case "bid":
                        bids.Add(level);
                        break;
                    case "ask":
                        asks.Add(level);
                        break;
                    default:
                        throw new FormatException($"Unexpected Coinbase book side '{side}'.");
                }
            }

            var kind = type switch
            {
                "snapshot" => BookUpdateKind.Snapshot,
                "update" => BookUpdateKind.Delta,
                _ => throw new FormatException($"Unexpected Coinbase l2_data event type '{type}'.")
            };

            deltas.Add(new BookDelta(
                Exchange.CoinbaseAdvancedTrade,
                _instrument,
                receivedAtUtc,
                receivedAtStopwatchTicks,
                eventTime,
                sequence: null,
                kind,
                bids,
                asks));
        }

        return deltas;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new FormatException($"Coinbase payload requires non-empty '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static DateTimeOffset ParseUtcTimestamp(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) ||
            timestamp.Offset != TimeSpan.Zero)
        {
            throw new FormatException($"Invalid Coinbase event timestamp '{value}'.");
        }

        return timestamp;
    }
}
