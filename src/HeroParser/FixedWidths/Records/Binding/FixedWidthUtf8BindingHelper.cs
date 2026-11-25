using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
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
    public static string Decode(ReadOnlySpan<byte> utf8) => new Utf8SpanParser(utf8).Decode();

    /// <summary>
    /// Checks whether the UTF-8 span is empty or whitespace-only.
    /// </summary>
    public static bool IsNullOrWhiteSpace(ReadOnlySpan<byte> utf8)
        => new Utf8SpanParser(utf8).IsNullOrWhiteSpace();

    public static bool TryParseInt32(ReadOnlySpan<byte> utf8, CultureInfo culture, out int value)
        => new Utf8SpanParser(utf8).TryParseInt32(culture, out value);

    public static bool TryParseInt64(ReadOnlySpan<byte> utf8, CultureInfo culture, out long value)
        => new Utf8SpanParser(utf8).TryParseInt64(culture, out value);

    public static bool TryParseInt16(ReadOnlySpan<byte> utf8, CultureInfo culture, out short value)
        => new Utf8SpanParser(utf8).TryParseInt16(culture, out value);

    public static bool TryParseByte(ReadOnlySpan<byte> utf8, CultureInfo culture, out byte value)
        => new Utf8SpanParser(utf8).TryParseByte(culture, out value);

    public static bool TryParseUInt32(ReadOnlySpan<byte> utf8, CultureInfo culture, out uint value)
        => new Utf8SpanParser(utf8).TryParseUInt32(culture, out value);

    public static bool TryParseUInt64(ReadOnlySpan<byte> utf8, CultureInfo culture, out ulong value)
        => new Utf8SpanParser(utf8).TryParseUInt64(culture, out value);

    public static bool TryParseUInt16(ReadOnlySpan<byte> utf8, CultureInfo culture, out ushort value)
        => new Utf8SpanParser(utf8).TryParseUInt16(culture, out value);

    public static bool TryParseSByte(ReadOnlySpan<byte> utf8, CultureInfo culture, out sbyte value)
        => new Utf8SpanParser(utf8).TryParseSByte(culture, out value);

    public static bool TryParseDecimal(ReadOnlySpan<byte> utf8, CultureInfo culture, out decimal value)
        => new Utf8SpanParser(utf8).TryParseDecimal(culture, NumberStyles.Number, out value);

    public static bool TryParseDouble(ReadOnlySpan<byte> utf8, CultureInfo culture, out double value)
        => new Utf8SpanParser(utf8).TryParseDouble(culture, NumberStyles.Float | NumberStyles.AllowThousands, out value);

    public static bool TryParseSingle(ReadOnlySpan<byte> utf8, CultureInfo culture, out float value)
        => new Utf8SpanParser(utf8).TryParseSingle(culture, NumberStyles.Float | NumberStyles.AllowThousands, out value);

    /// <summary>
    /// Parses a numeric column from chars with the culture, also accepting the ASCII <c>-</c> when the culture's
    /// negative sign is a minus with bidirectional marks (fa-IR, ar-SA, he-IL).
    /// </summary>
    public static bool TryParseNumber<TNumber>(ReadOnlySpan<char> chars, NumberStyles styles, IFormatProvider? provider, out TNumber value)
        where TNumber : System.Numerics.INumberBase<TNumber>
        => new CultureNumberParser(provider).TryParse(chars, styles, out value);

    public static bool TryParseBoolean(ReadOnlySpan<byte> utf8, out bool value)
        => new Utf8SpanParser(utf8).TryParseBoolean(out value);

    public static bool TryParseGuid(ReadOnlySpan<byte> utf8, out Guid value)
        => new Utf8SpanParser(utf8).TryParseGuid(out value);

    public static bool TryParseDateTime(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out DateTime value)
        => format is null
            ? new Utf8SpanParser(utf8).TryParseDateTime(culture, DateTimeStyles.None, out value)
            : new Utf8SpanParser(utf8).TryParseDateTimeExact(format, culture, DateTimeStyles.None, out value);

    public static bool TryParseDateTimeOffset(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out DateTimeOffset value)
        => format is null
            ? new Utf8SpanParser(utf8).TryParseDateTimeOffset(culture, DateTimeStyles.None, out value)
            : new Utf8SpanParser(utf8).TryParseDateTimeOffsetExact(format, culture, DateTimeStyles.None, out value);

    public static bool TryParseDateOnly(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out DateOnly value)
        => format is null
            ? new Utf8SpanParser(utf8).TryParseDateOnly(culture, DateTimeStyles.None, out value)
            : new Utf8SpanParser(utf8).TryParseDateOnlyExact(format, culture, DateTimeStyles.None, out value);

    public static bool TryParseTimeOnly(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out TimeOnly value)
        => format is null
            ? new Utf8SpanParser(utf8).TryParseTimeOnly(culture, DateTimeStyles.None, out value)
            : new Utf8SpanParser(utf8).TryParseTimeOnlyExact(format, culture, DateTimeStyles.None, out value);

    public static bool TryParseEnum<TEnum>(ReadOnlySpan<byte> utf8, out TEnum value)
        where TEnum : struct, Enum
        => new Utf8SpanParser(utf8).TryParseEnum(ignoreCase: true, out value);

    public static bool TryParseTimeSpan(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out TimeSpan value)
        => format is null
            ? new Utf8SpanParser(utf8).TryParseTimeSpan(culture, out value)
            : new Utf8SpanParser(utf8).TryParseTimeSpanExact(format, culture, out value);

    public static bool TryParseChar(ReadOnlySpan<byte> utf8, out char value)
        => new Utf8SpanParser(utf8).TryParseChar(out value);

    public static bool TryParseHalf(ReadOnlySpan<byte> utf8, CultureInfo culture, out Half value)
        => new Utf8SpanParser(utf8).TryParseHalf(culture, NumberStyles.Float | NumberStyles.AllowThousands, out value);

    public static bool TryParseInt128(ReadOnlySpan<byte> utf8, CultureInfo culture, out Int128 value)
        => new Utf8SpanParser(utf8).TryParseInt128(culture, out value);

    public static bool TryParseUInt128(ReadOnlySpan<byte> utf8, CultureInfo culture, out UInt128 value)
        => new Utf8SpanParser(utf8).TryParseUInt128(culture, out value);

    public static bool TryParseBigInteger(ReadOnlySpan<byte> utf8, CultureInfo culture, out BigInteger value)
        => new Utf8SpanParser(utf8).TryParseBigInteger(culture, out value);

    public static bool TryParseUri(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out Uri? value)
        => new Utf8SpanParser(utf8).TryParseUri(out value);

    public static bool TryParseBase64Bytes(ReadOnlySpan<byte> utf8, [NotNullWhen(true)] out byte[]? value)
        => new Utf8SpanParser(utf8).TryParseBase64Bytes(out value);

    public static bool TryParseIsomorphic<T>(ReadOnlySpan<byte> utf8, IFormatProvider? provider, [MaybeNullWhen(false)] out T value)
        where T : ISpanParsable<T>
        => new Utf8SpanParser(utf8).TryParseIsomorphic(provider, out value);

    public static bool TryParseUtf8Isomorphic<T>(ReadOnlySpan<byte> utf8, IFormatProvider? provider, [MaybeNullWhen(false)] out T value)
        where T : IUtf8SpanParsable<T>
        => new Utf8SpanParser(utf8).TryParseUtf8Isomorphic(provider, out value);
}

#pragma warning restore CS1591
