using System.Globalization;

namespace HeroParser.Vectors;

/// <summary>
/// Parses inline vector columns — the format AI/ML datasets use for pre-computed embeddings.
/// Accepted shapes (square brackets optional, separators may be comma, semicolon, or whitespace):
/// <c>[0.1, 0.2, 0.3]</c>, <c>0.1,0.2,0.3</c>, <c>0.1 0.2 0.3</c>, <c>[]</c>.
/// </summary>
/// <remarks>
/// Consecutive separators are tolerated (collapsed). When a culture whose decimal separator is
/// <c>,</c> is supplied, comma is treated as the decimal mark and not as a value separator; use
/// whitespace or <c>;</c> between elements in that case.
/// </remarks>
public static class VectorParser
{
    /// <summary>Parses a span of characters into a <see cref="float"/> vector.</summary>
    /// <exception cref="FormatException">Thrown when a token cannot be parsed.</exception>
    public static float[] ParseFloats(ReadOnlySpan<char> value, CultureInfo? culture = null)
    {
        if (!TryParseFloats(value, culture, out float[]? result))
            throw new FormatException($"Value is not a valid vector: '{value}'.");
        return result!;
    }

    /// <summary>Parses a span of characters into a <see cref="double"/> vector.</summary>
    /// <exception cref="FormatException">Thrown when a token cannot be parsed.</exception>
    public static double[] ParseDoubles(ReadOnlySpan<char> value, CultureInfo? culture = null)
    {
        if (!TryParseDoubles(value, culture, out double[]? result))
            throw new FormatException($"Value is not a valid vector: '{value}'.");
        return result!;
    }

    /// <summary>Tries to parse a span of characters into a <see cref="float"/> vector.</summary>
    public static bool TryParseFloats(ReadOnlySpan<char> value, out float[]? result)
        => TryParseFloats(value, culture: null, out result);

    /// <summary>Tries to parse a span of characters into a <see cref="float"/> vector using the supplied culture.</summary>
    public static bool TryParseFloats(ReadOnlySpan<char> value, CultureInfo? culture, out float[]? result)
    {
        CultureInfo c = culture ?? CultureInfo.InvariantCulture;
        bool commaIsDecimal = c.NumberFormat.NumberDecimalSeparator == ",";
        ReadOnlySpan<char> body = TrimBrackets(value);
        if (body.IsEmpty)
        {
            result = [];
            return true;
        }

        List<float> values = [];
        int tokenStart = -1;
        for (int i = 0; i <= body.Length; i++)
        {
            bool atEnd = i == body.Length;
            bool atSep = !atEnd && IsSeparator(body[i], commaIsDecimal);
            if (atEnd || atSep)
            {
                if (tokenStart >= 0)
                {
                    if (!float.TryParse(body[tokenStart..i], NumberStyles.Float, c, out float parsed))
                    {
                        result = null;
                        return false;
                    }
                    values.Add(parsed);
                    tokenStart = -1;
                }
            }
            else if (tokenStart < 0)
            {
                tokenStart = i;
            }
        }

        result = [.. values];
        return true;
    }

    /// <summary>Tries to parse a span of characters into a <see cref="double"/> vector.</summary>
    public static bool TryParseDoubles(ReadOnlySpan<char> value, out double[]? result)
        => TryParseDoubles(value, culture: null, out result);

    /// <summary>Tries to parse a span of characters into a <see cref="double"/> vector using the supplied culture.</summary>
    public static bool TryParseDoubles(ReadOnlySpan<char> value, CultureInfo? culture, out double[]? result)
    {
        CultureInfo c = culture ?? CultureInfo.InvariantCulture;
        bool commaIsDecimal = c.NumberFormat.NumberDecimalSeparator == ",";
        ReadOnlySpan<char> body = TrimBrackets(value);
        if (body.IsEmpty)
        {
            result = [];
            return true;
        }

        List<double> values = [];
        int tokenStart = -1;
        for (int i = 0; i <= body.Length; i++)
        {
            bool atEnd = i == body.Length;
            bool atSep = !atEnd && IsSeparator(body[i], commaIsDecimal);
            if (atEnd || atSep)
            {
                if (tokenStart >= 0)
                {
                    if (!double.TryParse(body[tokenStart..i], NumberStyles.Float, c, out double parsed))
                    {
                        result = null;
                        return false;
                    }
                    values.Add(parsed);
                    tokenStart = -1;
                }
            }
            else if (tokenStart < 0)
            {
                tokenStart = i;
            }
        }

        result = [.. values];
        return true;
    }

    private static ReadOnlySpan<char> TrimBrackets(ReadOnlySpan<char> value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']')
            return value[1..^1].Trim();
        return value;
    }

    private static bool IsSeparator(char c, bool commaIsDecimal)
    {
        if (c == ',') return !commaIsDecimal;
        return c == ';' || char.IsWhiteSpace(c);
    }
}
