using System.ComponentModel;
using System.Globalization;
using System.Text;
using HeroParser.Internal;

// Infrastructure helpers used exclusively by source-generated fixed-width binders in user assemblies.
// CS1591: members are documented on the class summary; per-method docs would be noise on a generated-only bridge.
#pragma warning disable CS1591

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// UTF-8 parsing bridge used exclusively by source-generated fixed-width binders. Not intended for direct use.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class FixedWidthUtf8BindingHelper
{
    /// <summary>
    /// Returns <see langword="true"/> when the encoding is UTF-8 compatible with the byte binders.
    /// </summary>
    public static bool IsUtf8Encoding(Encoding encoding)
        => Utf8SpanParser.IsUtf8Encoding(encoding);

    /// <summary>
    /// Decodes a UTF-8 span to a managed string.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> utf8) => Utf8SpanParser.Decode(utf8);

    /// <summary>
    /// Checks whether the UTF-8 span is empty or whitespace-only.
    /// </summary>
    public static bool IsNullOrWhiteSpace(ReadOnlySpan<byte> utf8)
        => Utf8SpanParser.IsNullOrWhiteSpace(utf8);

    public static bool TryParseInt32(ReadOnlySpan<byte> utf8, CultureInfo culture, out int value)
        => Utf8SpanParser.TryParseInt32(utf8, culture, out value);

    public static bool TryParseInt64(ReadOnlySpan<byte> utf8, CultureInfo culture, out long value)
        => Utf8SpanParser.TryParseInt64(utf8, culture, out value);

    public static bool TryParseInt16(ReadOnlySpan<byte> utf8, CultureInfo culture, out short value)
        => Utf8SpanParser.TryParseInt16(utf8, culture, out value);

    public static bool TryParseByte(ReadOnlySpan<byte> utf8, CultureInfo culture, out byte value)
        => Utf8SpanParser.TryParseByte(utf8, culture, out value);

    public static bool TryParseDecimal(ReadOnlySpan<byte> utf8, CultureInfo culture, out decimal value)
        => Utf8SpanParser.TryParseDecimal(utf8, culture, NumberStyles.Number, out value);

    public static bool TryParseDouble(ReadOnlySpan<byte> utf8, CultureInfo culture, out double value)
        => Utf8SpanParser.TryParseDouble(utf8, culture, NumberStyles.Float | NumberStyles.AllowThousands, out value);

    public static bool TryParseSingle(ReadOnlySpan<byte> utf8, CultureInfo culture, out float value)
        => Utf8SpanParser.TryParseSingle(utf8, culture, NumberStyles.Float | NumberStyles.AllowThousands, out value);

    /// <summary>
    /// Parses a numeric column from chars with the culture, also accepting the ASCII <c>-</c> when the culture's
    /// negative sign is a minus with bidirectional marks (fa-IR, ar-SA, he-IL).
    /// </summary>
    public static bool TryParseNumber<TNumber>(ReadOnlySpan<char> chars, NumberStyles styles, IFormatProvider? provider, out TNumber value)
        where TNumber : System.Numerics.INumberBase<TNumber>
        => CultureNumberParser.TryParse(chars, styles, provider, out value);

    public static bool TryParseBoolean(ReadOnlySpan<byte> utf8, out bool value)
        => Utf8SpanParser.TryParseBoolean(utf8, out value);

    public static bool TryParseGuid(ReadOnlySpan<byte> utf8, out Guid value)
        => Utf8SpanParser.TryParseGuid(utf8, out value);

    public static bool TryParseDateTime(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out DateTime value)
        => format is null
            ? Utf8SpanParser.TryParseDateTime(utf8, culture, DateTimeStyles.None, out value)
            : Utf8SpanParser.TryParseDateTimeExact(utf8, format, culture, DateTimeStyles.None, out value);

    public static bool TryParseDateTimeOffset(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out DateTimeOffset value)
        => format is null
            ? Utf8SpanParser.TryParseDateTimeOffset(utf8, culture, DateTimeStyles.None, out value)
            : Utf8SpanParser.TryParseDateTimeOffsetExact(utf8, format, culture, DateTimeStyles.None, out value);

    public static bool TryParseDateOnly(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out DateOnly value)
        => format is null
            ? Utf8SpanParser.TryParseDateOnly(utf8, culture, DateTimeStyles.None, out value)
            : Utf8SpanParser.TryParseDateOnlyExact(utf8, format, culture, DateTimeStyles.None, out value);

    public static bool TryParseTimeOnly(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out TimeOnly value)
        => format is null
            ? Utf8SpanParser.TryParseTimeOnly(utf8, culture, DateTimeStyles.None, out value)
            : Utf8SpanParser.TryParseTimeOnlyExact(utf8, format, culture, DateTimeStyles.None, out value);

    public static bool TryParseEnum<TEnum>(ReadOnlySpan<byte> utf8, out TEnum value)
        where TEnum : struct, Enum
        => Utf8SpanParser.TryParseEnum(utf8, ignoreCase: true, out value);
}

#pragma warning restore CS1591
