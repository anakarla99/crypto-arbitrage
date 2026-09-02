using System.Globalization;

namespace CryptoArbitrage.Domain;

/// <summary>Non-negative fixed-point integer units. The owning venue precision supplies the scale.</summary>
public readonly record struct FixedPoint(long Units)
{
    public static FixedPoint Parse(string text, int scale, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, parameterName);

        if (scale is < 0 or > 12 || text[0] is '-' or '+')
        {
            throw new ArgumentException("Value must be an unsigned fixed-point value with a supported scale.", parameterName);
        }

        var separatorIndex = text.IndexOf('.');
        if (separatorIndex != -1 && separatorIndex != text.LastIndexOf('.'))
        {
            throw new ArgumentException("Value must contain at most one decimal separator.", parameterName);
        }

        var wholePart = separatorIndex == -1 ? text : text[..separatorIndex];
        var fractionalPart = separatorIndex == -1 ? string.Empty : text[(separatorIndex + 1)..];
        if (wholePart.Length == 0 || !wholePart.All(char.IsAsciiDigit) || !fractionalPart.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Value must contain ASCII digits only.", parameterName);
        }

        if (fractionalPart.Length > scale && fractionalPart[scale..].Any(character => character != '0'))
        {
            throw new ArgumentException($"Value has non-zero precision beyond scale {scale}.", parameterName);
        }

        var normalizedFraction = fractionalPart.Length >= scale
            ? fractionalPart[..scale]
            : fractionalPart.PadRight(scale, '0');
        var combined = string.Concat(wholePart, normalizedFraction);

        if (!long.TryParse(combined, NumberStyles.None, CultureInfo.InvariantCulture, out var units))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value cannot be represented as Int64 fixed-point units.");
        }

        return new FixedPoint(units);
    }

    public string ToDisplayString(int scale)
    {
        if (scale is < 0 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var digits = Units.ToString(CultureInfo.InvariantCulture);
        if (scale == 0)
        {
            return digits;
        }

        var padded = digits.PadLeft(scale + 1, '0');
        return string.Concat(padded.AsSpan(0, padded.Length - scale), ".", padded.AsSpan(padded.Length - scale));
    }
}
