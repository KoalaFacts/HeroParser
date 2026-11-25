using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using HeroParser.Internal;

namespace HeroParser.FixedWidths;

/// <summary>
/// Represents a fixed-width field backed by the original character span.
/// </summary>
/// <remarks>Operations avoid allocations unless explicitly noted (e.g., <see cref="ToString"/>).</remarks>
public readonly ref struct FixedWidthCharSpanColumn
{
    private readonly ReadOnlySpan<char> chars;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal FixedWidthCharSpanColumn(ReadOnlySpan<char> chars)
    {
        this.chars = chars;
    }

    /// <summary>Gets the raw UTF-16 span that composes the field.</summary>
    public ReadOnlySpan<char> CharSpan => chars;

    /// <summary>Gets the field length in characters.</summary>
    public int Length => chars.Length;

    /// <summary>Gets a value indicating whether the field is empty.</summary>
    public bool IsEmpty => chars.IsEmpty;

    /// <summary>Creates a <see cref="string"/> representation of the field.</summary>
    public override string ToString() => new(chars);

    /// <summary>Parses the field using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
        => T.Parse(chars, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>([MaybeNullWhen(false)] out T result) where T : ISpanParsable<T>
        => T.TryParse(chars, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field using <see cref="ISpanParsable{T}"/> and the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>([MaybeNullWhen(false)] out T result, IFormatProvider? provider) where T : ISpanParsable<T>
        => T.TryParse(chars, provider, out result);

    /// <summary>Attempts to parse the field using <see cref="IUtf8SpanParsable{T}"/> and the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseUtf8<T>([MaybeNullWhen(false)] out T result, IFormatProvider? provider = null) where T : IUtf8SpanParsable<T>
        => Utf8SpanParser.TryParseUtf8FromChars(chars, provider ?? CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as an <see cref="int"/> using invariant culture.</summary>
    public bool TryParseInt32(out int result)
        => int.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="short"/> using invariant culture.</summary>
    public bool TryParseInt16(out short result)
        => short.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="uint"/> using invariant culture.</summary>
    public bool TryParseUInt32(out uint result)
        => uint.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="ushort"/> using invariant culture.</summary>
    public bool TryParseUInt16(out ushort result)
        => ushort.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="long"/> using invariant culture.</summary>
    public bool TryParseInt64(out long result)
        => long.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="ulong"/> using invariant culture.</summary>
    public bool TryParseUInt64(out ulong result)
        => ulong.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="double"/> using invariant culture.</summary>
    public bool TryParseDouble(out double result)
        => double.TryParse(chars, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="float"/> using invariant culture.</summary>
    public bool TryParseSingle(out float result)
        => float.TryParse(chars, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="decimal"/> using invariant culture.</summary>
    public bool TryParseDecimal(out decimal result)
        => decimal.TryParse(chars, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="bool"/>.</summary>
    public bool TryParseBoolean(out bool result)
        => bool.TryParse(chars, out result);

    /// <summary>Attempts to parse the field as a <see cref="DateTime"/> using invariant culture.</summary>
    public bool TryParseDateTime(out DateTime result)
        => TryParseDateTime(out result, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTime"/> using the provided culture and styles.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTime.TryParse(chars, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTime"/> using an exact format string, culture, and styles.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTime.TryParseExact(chars, format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTime"/> using an exact format with invariant culture.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, string format)
        => TryParseDateTime(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the field as a <see cref="DateTimeOffset"/> using invariant culture.</summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result)
        => TryParseDateTimeOffset(out result, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTimeOffset"/> using the provided culture and styles.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTimeOffset.TryParse(chars, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTimeOffset"/> using exact format, culture, and styles.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTimeOffset.TryParseExact(chars, format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTimeOffset"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format)
        => TryParseDateTimeOffset(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateOnly"/> using exact format, culture, and styles.
    /// </summary>
    public bool TryParseDateOnly(out DateOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateOnly.TryParseExact(chars, format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateOnly"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseDateOnly(out DateOnly result, string format)
        => TryParseDateOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>
    /// Attempts to parse the field as a <see cref="TimeOnly"/> using exact format, culture, and styles.
    /// </summary>
    public bool TryParseTimeOnly(out TimeOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => TimeOnly.TryParseExact(chars, format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="TimeOnly"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseTimeOnly(out TimeOnly result, string format)
        => TryParseTimeOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the field as a <see cref="DateOnly"/> using invariant culture.</summary>
    public bool TryParseDateOnly(out DateOnly result)
        => TryParseDateOnly(out result, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the field as a <see cref="DateOnly"/> using the provided culture and styles.</summary>
    public bool TryParseDateOnly(out DateOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateOnly.TryParse(chars, provider, styles, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeOnly"/> using invariant culture.</summary>
    public bool TryParseTimeOnly(out TimeOnly result)
        => TryParseTimeOnly(out result, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the field as a <see cref="TimeOnly"/> using the provided culture and styles.</summary>
    public bool TryParseTimeOnly(out TimeOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => TimeOnly.TryParse(chars, provider, styles, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeSpan"/> using invariant culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result)
        => TimeSpan.TryParse(chars, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeSpan"/> using the provided culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result, IFormatProvider? provider)
        => TimeSpan.TryParse(chars, provider, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeSpan"/> using an exact format string and culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result, string format, IFormatProvider? provider)
        => TimeSpan.TryParseExact(chars, format, provider, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeSpan"/> using an exact format string with invariant culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result, string format)
        => TimeSpan.TryParseExact(chars, format, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as a single <see cref="char"/>.</summary>
    public bool TryParseChar(out char result)
    {
        if (chars.Length == 1)
        {
            result = chars[0];
            return true;
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the field as a <see cref="Half"/> using invariant culture.</summary>
    public bool TryParseHalf(out Half result)
        => TryParseHalf(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field as a <see cref="Half"/> using the provided format provider.</summary>
    public bool TryParseHalf(out Half result, IFormatProvider? provider)
        => Half.TryParse(chars, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);

    /// <summary>Attempts to parse the field as an <see cref="Int128"/> using invariant culture.</summary>
    public bool TryParseInt128(out Int128 result)
        => TryParseInt128(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field as an <see cref="Int128"/> using the provided format provider.</summary>
    public bool TryParseInt128(out Int128 result, IFormatProvider? provider)
        => Int128.TryParse(chars, NumberStyles.Integer, provider, out result);

    /// <summary>Attempts to parse the field as a <see cref="UInt128"/> using invariant culture.</summary>
    public bool TryParseUInt128(out UInt128 result)
        => TryParseUInt128(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field as a <see cref="UInt128"/> using the provided format provider.</summary>
    public bool TryParseUInt128(out UInt128 result, IFormatProvider? provider)
        => UInt128.TryParse(chars, NumberStyles.Integer, provider, out result);

    /// <summary>Attempts to parse the field as a <see cref="BigInteger"/> using invariant culture.</summary>
    public bool TryParseBigInteger(out BigInteger result)
        => TryParseBigInteger(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field as a <see cref="BigInteger"/> using the provided format provider.</summary>
    public bool TryParseBigInteger(out BigInteger result, IFormatProvider? provider)
        => BigInteger.TryParse(chars, NumberStyles.Integer, provider, out result);

    /// <summary>Attempts to parse a non-empty field as a <see cref="Uri"/>.</summary>
    public bool TryParseUri([NotNullWhen(true)] out Uri? result)
    {
        if (chars.IsEmpty || chars.IsWhiteSpace())
        {
            result = null;
            return false;
        }
        return Uri.TryCreate(new string(chars.Trim()), UriKind.RelativeOrAbsolute, out result);
    }

    /// <summary>Attempts to parse a Base64-encoded field into a byte array.</summary>
    public bool TryParseBase64Bytes([NotNullWhen(true)] out byte[]? result)
        => Utf8SpanParser.TryParseBase64Chars(chars, out result);

    /// <summary>Attempts to parse the field as a <see cref="TimeZoneInfo"/> from its identifier.</summary>
    public bool TryParseTimeZoneInfo(out TimeZoneInfo result)
    {
        if (chars.IsEmpty)
        {
            result = default!;
            return false;
        }

        try
        {
            result = TimeZoneInfo.FindSystemTimeZoneById(new string(chars));
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

    /// <summary>Attempts to parse the field as a <see cref="Guid"/>.</summary>
    public bool TryParseGuid(out Guid result)
        => Guid.TryParse(chars, out result);

    /// <summary>Attempts to parse the field as a <see cref="byte"/> using invariant culture.</summary>
    public bool TryParseByte(out byte result)
        => byte.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the field as an <see cref="sbyte"/> using invariant culture.</summary>
    public bool TryParseSByte(out sbyte result)
        => sbyte.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// Attempts to parse the field as an enum of type <typeparamref name="TEnum"/> using case-insensitive matching.
    /// </summary>
    public bool TryParseEnum<TEnum>(out TEnum result) where TEnum : struct, Enum
        => Enum.TryParse(chars, ignoreCase: true, out result);

    /// <summary>Compares the field with a string using ordinal semantics.</summary>
    public bool Equals(string? other)
        => other is not null && other.AsSpan().SequenceEqual(chars);
}
