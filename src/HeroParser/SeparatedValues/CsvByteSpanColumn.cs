using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a UTF-8 encoded CSV column backed by the original input buffer.
/// </summary>
/// <remarks>
/// All operations avoid allocations unless explicitly stated (e.g., <see cref="ToString"/> or <see cref="UnquoteToString(byte)"/>).
/// </remarks>
public readonly ref struct CsvByteSpanColumn
{
    private readonly ReadOnlySpan<byte> utf8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvByteSpanColumn(ReadOnlySpan<byte> utf8)
    {
        this.utf8 = utf8;
    }

    /// <summary>Gets the raw UTF-8 bytes that compose the column.</summary>
    public ReadOnlySpan<byte> Utf8Span => utf8;
    /// <summary>Gets the column length in bytes.</summary>
    public int Length => utf8.Length;
    /// <summary>Gets a value indicating whether the column is empty.</summary>
    public bool IsEmpty => utf8.IsEmpty;

    /// <summary>Decodes the column into a UTF-16 <see cref="string"/>.</summary>
    public override string ToString() => Encoding.UTF8.GetString(utf8);

    /// <summary>Parses the column using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
        => T.Parse(Decode(), CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>(out T? result) where T : ISpanParsable<T>
        => T.TryParse(Decode(), CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the column as an <see cref="int"/> using invariant culture.</summary>
    public bool TryParseInt32(out int result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="short"/> using invariant culture.</summary>
    public bool TryParseInt16(out short result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as an <see cref="uint"/> using invariant culture.</summary>
    public bool TryParseUInt32(out uint result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="ushort"/> using invariant culture.</summary>
    public bool TryParseUInt16(out ushort result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="long"/> using invariant culture.</summary>
    public bool TryParseInt64(out long result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="ulong"/> using invariant culture.</summary>
    public bool TryParseUInt64(out ulong result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="double"/> using invariant culture.</summary>
    public bool TryParseDouble(out double result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="float"/> using invariant culture.</summary>
    public bool TryParseSingle(out float result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="decimal"/> using invariant culture.</summary>
    public bool TryParseDecimal(out decimal result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="bool"/> using invariant culture.</summary>
    public bool TryParseBoolean(out bool result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="DateTime"/> using invariant culture.</summary>
    public bool TryParseDateTime(out DateTime result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTime"/> using the provided culture and styles.
    /// Falls back to decoding to UTF-16 since Utf8Parser is invariant-culture only.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (IsInvariant(provider) && styles == DateTimeStyles.None)
            return Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

        return DateTime.TryParse(Decode(), provider, styles, out result);
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTime"/> using an exact format string, culture, and styles.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateTime(out DateTime result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTime.TryParseExact(Decode(), format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTime"/> using an exact format, culture, and styles.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, string format)
        => DateTime.TryParseExact(Decode(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>Attempts to parse the column as a <see cref="DateTimeOffset"/> using invariant culture.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateTimeOffset(out DateTimeOffset result)
        => DateTimeOffset.TryParse(Decode(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTimeOffset"/> using the provided culture and styles.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTimeOffset.TryParse(Decode(), provider, styles, out result);

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTimeOffset"/> using an exact format, culture, and styles.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateTimeOffset.TryParseExact(Decode(), format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTimeOffset"/> using an exact format with invariant culture.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format)
        => TryParseDateTimeOffset(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the column as a <see cref="DateOnly"/> using invariant culture.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateOnly(out DateOnly result)
        => DateOnly.TryParse(Decode(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>Attempts to parse the column as a <see cref="DateOnly"/> using the provided culture and styles.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateOnly(out DateOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateOnly.TryParse(Decode(), provider, styles, out result);

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateOnly"/> using an exact format, culture, and styles.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseDateOnly(out DateOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => DateOnly.TryParseExact(Decode(), format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateOnly"/> using an exact format with invariant culture.
    /// </summary>
    public bool TryParseDateOnly(out DateOnly result, string format)
        => TryParseDateOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the column as a <see cref="TimeOnly"/> using invariant culture.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseTimeOnly(out TimeOnly result)
        => TimeOnly.TryParse(Decode(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>Attempts to parse the column as a <see cref="TimeOnly"/> using the provided culture and styles.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseTimeOnly(out TimeOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => TimeOnly.TryParse(Decode(), provider, styles, out result);

    /// <summary>
    /// Attempts to parse the column as a <see cref="TimeOnly"/> using an exact format, culture, and styles.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16.</remarks>
    public bool TryParseTimeOnly(out TimeOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
        => TimeOnly.TryParseExact(Decode(), format, provider, styles, out result);

    /// <summary>
    /// Attempts to parse the column as a <see cref="TimeOnly"/> using an exact format with invariant culture.
    /// </summary>
    public bool TryParseTimeOnly(out TimeOnly result, string format)
        => TryParseTimeOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the column as a <see cref="TimeZoneInfo"/> from its identifier.</summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16 before lookup.</remarks>
    public bool TryParseTimeZoneInfo(out TimeZoneInfo result)
    {
        if (utf8.IsEmpty)
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

    /// <summary>Attempts to parse the column as a <see cref="Guid"/>.</summary>
    public bool TryParseGuid(out Guid result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="byte"/> using invariant culture.</summary>
    public bool TryParseByte(out byte result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as an <see cref="sbyte"/> using invariant culture.</summary>
    public bool TryParseSByte(out sbyte result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>
    /// Attempts to parse the column as an enum of type <typeparamref name="TEnum"/> using case-insensitive matching.
    /// Falls back to UTF-16 decode since Enum.TryParse operates on strings.
    /// </summary>
    /// <remarks>Allocates to decode the UTF-8 span to UTF-16 before parsing.</remarks>
    public bool TryParseEnum<TEnum>(out TEnum result) where TEnum : struct, Enum
        => Enum.TryParse(Decode(), ignoreCase: true, out result);

    /// <summary>Compares the column with a string using ordinal semantics.</summary>
    public bool Equals(string? other)
        => other is not null && other.AsSpan().SequenceEqual(ToString());

    /// <summary>Returns the underlying span with surrounding quotes removed, if present.</summary>
    /// <param name="quote">Quote character (defaults to double quote).</param>
    public ReadOnlySpan<byte> Unquote(byte quote = (byte)'"')
    {
        var span = utf8;
        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            return span[1..^1];
        }
        return span;
    }

    /// <summary>Unquotes and decodes the column into a <see cref="string"/>, collapsing doubled quotes.</summary>
    /// <param name="quote">Quote character (defaults to double quote).</param>
    public string UnquoteToString(byte quote = (byte)'"')
    {
        var span = utf8;

        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            var inner = span[1..^1];

            if (inner.Contains(quote))
            {
                return Encoding.UTF8.GetString(inner).Replace(new string((char)quote, 2), new string((char)quote, 1));
            }

            return Encoding.UTF8.GetString(inner);
        }

        return Encoding.UTF8.GetString(span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string Decode() => Encoding.UTF8.GetString(utf8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInvariant(IFormatProvider? provider)
        => provider is null || Equals(provider, CultureInfo.InvariantCulture);
}
