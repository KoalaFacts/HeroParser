using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.Internal;

namespace HeroParser.FixedWidths;

/// <summary>
/// Represents a fixed-width field backed by the original UTF-8 byte span.
/// </summary>
/// <remarks>Operations avoid allocations unless explicitly noted (e.g., <see cref="ToString"/>).</remarks>
public readonly ref struct FixedWidthByteSpanColumn
{
    private readonly ReadOnlySpan<byte> bytes;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal FixedWidthByteSpanColumn(ReadOnlySpan<byte> bytes)
    {
        this.bytes = bytes;
    }

    /// <summary>Gets the raw UTF-8 byte span that composes the field.</summary>
    public ReadOnlySpan<byte> ByteSpan => bytes;

    /// <summary>Gets the field length in bytes.</summary>
    public int Length => bytes.Length;

    /// <summary>Gets a value indicating whether the field is empty.</summary>
    public bool IsEmpty => bytes.IsEmpty;

    /// <summary>Creates a <see cref="string"/> representation of the field (UTF-8 decoded).</summary>
    public override string ToString() => Encoding.UTF8.GetString(bytes);

    /// <summary>Parses the field using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
        => new Utf8SpanParser(bytes).ParseIsomorphic<T>(CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>([MaybeNullWhen(false)] out T result) where T : ISpanParsable<T>
        => new Utf8SpanParser(bytes).TryParseIsomorphic<T>(CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field using <see cref="ISpanParsable{T}"/> and the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>([MaybeNullWhen(false)] out T result, IFormatProvider? provider) where T : ISpanParsable<T>
        => new Utf8SpanParser(bytes).TryParseIsomorphic<T>(provider, out result);

    /// <summary>Attempts to parse the field directly using <see cref="IUtf8SpanParsable{T}"/> and the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseUtf8<T>([MaybeNullWhen(false)] out T result, IFormatProvider? provider = null) where T : IUtf8SpanParsable<T>
        => new Utf8SpanParser(bytes).TryParseUtf8Isomorphic(provider ?? CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as an <see cref="int"/> using invariant culture.</summary>
    public bool TryParseInt32(out int result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="short"/> using invariant culture.</summary>
    public bool TryParseInt16(out short result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="uint"/> using invariant culture.</summary>
    public bool TryParseUInt32(out uint result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="ushort"/> using invariant culture.</summary>
    public bool TryParseUInt16(out ushort result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="long"/> using invariant culture.</summary>
    public bool TryParseInt64(out long result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="ulong"/> using invariant culture.</summary>
    public bool TryParseUInt64(out ulong result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="double"/> using invariant culture.</summary>
    public bool TryParseDouble(out double result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="float"/> using invariant culture.</summary>
    public bool TryParseSingle(out float result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="decimal"/> using invariant culture.</summary>
    public bool TryParseDecimal(out decimal result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="bool"/>.</summary>
    public bool TryParseBoolean(out bool result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="byte"/> using invariant culture.</summary>
    public bool TryParseByte(out byte result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as an <see cref="sbyte"/> using invariant culture.</summary>
    public bool TryParseSByte(out sbyte result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="Guid"/>.</summary>
    public bool TryParseGuid(out Guid result)
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>Attempts to parse the field as a <see cref="DateTime"/> using invariant culture.</summary>
    public bool TryParseDateTime(out DateTime result)
        => new Utf8SpanParser(bytes).TryParseDateTime(CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTime"/> using the provided culture and styles.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => new Utf8SpanParser(bytes).TryParseDateTime(provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTime"/> using an exact format string.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => new Utf8SpanParser(bytes).TryParseDateTimeExact(format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTime"/> using an exact format with invariant culture.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, string format)
        => new Utf8SpanParser(bytes).TryParseDateTimeExact(format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>Attempts to parse the field as a <see cref="DateTimeOffset"/> using invariant culture.</summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result)
        => new Utf8SpanParser(bytes).TryParseDateTimeOffset(CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTimeOffset"/> using the provided culture and styles.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => new Utf8SpanParser(bytes).TryParseDateTimeOffset(provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTimeOffset"/> using an exact format.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => new Utf8SpanParser(bytes).TryParseDateTimeOffsetExact(format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTimeOffset"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format)
        => TryParseDateTimeOffset(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the field as a <see cref="DateOnly"/> using invariant culture.</summary>
    public bool TryParseDateOnly(out DateOnly result)
        => new Utf8SpanParser(bytes).TryParseDateOnly(CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateOnly"/> using the provided culture and styles.
    /// </summary>
    public bool TryParseDateOnly(out DateOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => new Utf8SpanParser(bytes).TryParseDateOnly(provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateOnly"/> using an exact format.
    /// </summary>
    public bool TryParseDateOnly(out DateOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => new Utf8SpanParser(bytes).TryParseDateOnlyExact(format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateOnly"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseDateOnly(out DateOnly result, string format)
        => TryParseDateOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the field as a <see cref="TimeOnly"/> using invariant culture.</summary>
    public bool TryParseTimeOnly(out TimeOnly result)
        => new Utf8SpanParser(bytes).TryParseTimeOnly(CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="TimeOnly"/> using the provided culture and styles.
    /// </summary>
    public bool TryParseTimeOnly(out TimeOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => new Utf8SpanParser(bytes).TryParseTimeOnly(provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="TimeOnly"/> using an exact format.
    /// </summary>
    public bool TryParseTimeOnly(out TimeOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => new Utf8SpanParser(bytes).TryParseTimeOnlyExact(format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="TimeOnly"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseTimeOnly(out TimeOnly result, string format)
        => TryParseTimeOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the field as a <see cref="TimeSpan"/> using invariant culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result)
        => new Utf8SpanParser(bytes).TryParseTimeSpan(CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeSpan"/> using the provided culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result, IFormatProvider? provider)
        => new Utf8SpanParser(bytes).TryParseTimeSpan(provider, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeSpan"/> using an exact format and culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result, string format, IFormatProvider? provider)
        => new Utf8SpanParser(bytes).TryParseTimeSpanExact(format, provider, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeSpan"/> using an exact format with invariant culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result, string format)
        => TryParseTimeSpan(out result, format, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field as a single <see cref="char"/>.</summary>
    public bool TryParseChar(out char result)
        => new Utf8SpanParser(bytes).TryParseChar(out result);

    /// <summary>Attempts to parse the field as a <see cref="Half"/> using invariant culture.</summary>
    public bool TryParseHalf(out Half result)
        => TryParseHalf(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field as a <see cref="Half"/> using the provided culture.</summary>
    public bool TryParseHalf(out Half result, IFormatProvider? provider)
        => new Utf8SpanParser(bytes).TryParseHalf(provider, NumberStyles.Float | NumberStyles.AllowThousands, out result);

    /// <summary>Attempts to parse the field as an <see cref="Int128"/> using invariant culture.</summary>
    public bool TryParseInt128(out Int128 result)
        => TryParseInt128(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field as an <see cref="Int128"/> using the provided culture.</summary>
    public bool TryParseInt128(out Int128 result, IFormatProvider? provider)
        => new Utf8SpanParser(bytes).TryParseInt128(provider, out result);

    /// <summary>Attempts to parse the field as a <see cref="UInt128"/> using invariant culture.</summary>
    public bool TryParseUInt128(out UInt128 result)
        => TryParseUInt128(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field as a <see cref="UInt128"/> using the provided culture.</summary>
    public bool TryParseUInt128(out UInt128 result, IFormatProvider? provider)
        => new Utf8SpanParser(bytes).TryParseUInt128(provider, out result);

    /// <summary>Attempts to parse the field as a <see cref="BigInteger"/> using invariant culture.</summary>
    public bool TryParseBigInteger(out BigInteger result)
        => TryParseBigInteger(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field as a <see cref="BigInteger"/> using the provided culture.</summary>
    public bool TryParseBigInteger(out BigInteger result, IFormatProvider? provider)
        => new Utf8SpanParser(bytes).TryParseBigInteger(provider, out result);

    /// <summary>Attempts to parse a non-empty field as a <see cref="Uri"/>.</summary>
    public bool TryParseUri([NotNullWhen(true)] out Uri? result)
        => new Utf8SpanParser(bytes).TryParseUri(out result);

    /// <summary>Attempts to parse a Base64-encoded field into a byte array.</summary>
    public bool TryParseBase64Bytes([NotNullWhen(true)] out byte[]? result)
        => new Utf8SpanParser(bytes).TryParseBase64Bytes(out result);

    /// <summary>
    /// Attempts to parse the field as an enum of type <typeparamref name="TEnum"/> using case-insensitive matching.
    /// </summary>
    public bool TryParseEnum<TEnum>(out TEnum result) where TEnum : struct, Enum
        => new Utf8SpanParser(bytes).TryParseEnum(ignoreCase: true, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeZoneInfo"/> from its identifier.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16 before lookup.</remarks>
    public bool TryParseTimeZoneInfo(out TimeZoneInfo result)
    {
        if (bytes.IsEmpty)
        {
            result = default!;
            return false;
        }

        var id = Decode();
        try
        {
            result = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            result = default!;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            result = default!;
            return false;
        }
    }

    /// <summary>Compares the field with a string using ordinal semantics.</summary>
    public bool Equals(string? other)
        => new Utf8SpanParser(bytes).EqualsString(other, StringComparison.Ordinal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string Decode() => Encoding.UTF8.GetString(bytes);
}
