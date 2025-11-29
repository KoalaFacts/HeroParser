using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

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
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
        => T.Parse(Decode(), CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the field using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>(out T? result) where T : ISpanParsable<T>
        => T.TryParse(Decode(), CultureInfo.InvariantCulture, out result);

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
        => Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTime"/> using the provided culture and styles.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16 for non-invariant cultures.</remarks>
    public bool TryParseDateTime(out DateTime result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (IsInvariant(provider) && styles == DateTimeStyles.None)
            return Utf8Parser.TryParse(bytes, out result, out int consumed) && consumed == bytes.Length;

        return DateTime.TryParse(Decode(), provider, styles, out result);
    }

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTime"/> using an exact format string.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateTime(out DateTime result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTime.TryParseExact(Decode(), format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTime"/> using an exact format with invariant culture.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, string format)
        => DateTime.TryParseExact(Decode(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>Attempts to parse the field as a <see cref="DateTimeOffset"/> using invariant culture.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateTimeOffset(out DateTimeOffset result)
        => DateTimeOffset.TryParse(Decode(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTimeOffset"/> using the provided culture and styles.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTimeOffset.TryParse(Decode(), provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTimeOffset"/> using an exact format.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTimeOffset.TryParseExact(Decode(), format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateTimeOffset"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format)
        => TryParseDateTimeOffset(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the field as a <see cref="DateOnly"/> using invariant culture.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateOnly(out DateOnly result)
        => DateOnly.TryParse(Decode(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateOnly"/> using the provided culture and styles.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateOnly(out DateOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateOnly.TryParse(Decode(), provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateOnly"/> using an exact format.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateOnly(out DateOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateOnly.TryParseExact(Decode(), format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="DateOnly"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseDateOnly(out DateOnly result, string format)
        => TryParseDateOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the field as a <see cref="TimeOnly"/> using invariant culture.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseTimeOnly(out TimeOnly result)
        => TimeOnly.TryParse(Decode(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="TimeOnly"/> using the provided culture and styles.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseTimeOnly(out TimeOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => TimeOnly.TryParse(Decode(), provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="TimeOnly"/> using an exact format.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseTimeOnly(out TimeOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => TimeOnly.TryParseExact(Decode(), format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the field as a <see cref="TimeOnly"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseTimeOnly(out TimeOnly result, string format)
        => TryParseTimeOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>
    /// Attempts to parse the field as an enum of type <typeparamref name="TEnum"/> using case-insensitive matching.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16 before parsing.</remarks>
    public bool TryParseEnum<TEnum>(out TEnum result) where TEnum : struct, Enum
        => Enum.TryParse(Decode(), ignoreCase: true, out result);

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
        => other is not null && other.AsSpan().SequenceEqual(Decode());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string Decode() => Encoding.UTF8.GetString(bytes);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInvariant(IFormatProvider? provider)
        => provider is null || Equals(provider, CultureInfo.InvariantCulture);
}
