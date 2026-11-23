using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using HeroParser.Internal;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Rows;

/// <summary>
/// Represents a CSV column backed by the original span data.
/// </summary>
/// <typeparam name="T">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
/// <remarks>
/// <para>
/// Operations avoid allocations unless explicitly noted (e.g., <see cref="ToString"/>).
/// Uses typeof(T) specialization for optimal performance - JIT eliminates dead branches.
/// </para>
/// <para>
/// Thread-Safety: This is a ref struct that wraps stack-allocated or pooled memory and cannot be
/// shared across threads by design. Each instance is valid only within its original scope.
/// </para>
/// </remarks>
public readonly ref struct CsvColumn<T> where T : unmanaged, IEquatable<T>
{
    private readonly ReadOnlySpan<T> data;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvColumn(ReadOnlySpan<T> data)
    {
        this.data = data;
    }

    /// <summary>Gets the raw span that composes the column.</summary>
    public ReadOnlySpan<T> Span => data;

    /// <summary>Gets the column length in elements.</summary>
    public int Length => data.Length;

    /// <summary>Gets a value indicating whether the column is empty.</summary>
    public bool IsEmpty => data.IsEmpty;

    /// <summary>Creates a <see cref="string"/> representation of the column.</summary>
    public override string ToString()
    {
        if (typeof(T) == typeof(char))
        {
            return new string(GetCharSpan());
        }
        if (typeof(T) == typeof(byte))
        {
            return Encoding.UTF8.GetString(GetByteSpan());
        }
        throw new NotSupportedException($"Element type {typeof(T)} is not supported.");
    }

    /// <summary>Parses the column using <see cref="ISpanParsable{TResult}"/> and invariant culture.</summary>
    /// <remarks>For byte spans, uses stack allocation for fields up to 256 UTF-16 characters.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Parse<TResult>() where TResult : ISpanParsable<TResult>
        => Parse<TResult>(CultureInfo.InvariantCulture);

    /// <summary>Parses the column using <see cref="ISpanParsable{TResult}"/> and the specified format provider.</summary>
    /// <remarks>For byte spans, uses stack allocation for fields up to 256 UTF-16 characters.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Parse<TResult>(IFormatProvider? provider) where TResult : ISpanParsable<TResult>
    {
        if (typeof(T) == typeof(char))
        {
            return TResult.Parse(GetCharSpan(), provider);
        }
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).ParseIsomorphic<TResult>(provider);
        }
        throw new NotSupportedException($"Element type {typeof(T)} is not supported.");
    }

    /// <summary>Attempts to parse the column using <see cref="ISpanParsable{TResult}"/> and invariant culture.</summary>
    /// <remarks>For byte spans, uses stack allocation for fields up to 256 UTF-16 characters.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<TResult>([MaybeNullWhen(false)] out TResult result) where TResult : ISpanParsable<TResult>
        => TryParse(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column using <see cref="ISpanParsable{TResult}"/> and the specified format provider.</summary>
    /// <remarks>For byte spans, uses stack allocation for fields up to 256 UTF-16 characters.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<TResult>([MaybeNullWhen(false)] out TResult result, IFormatProvider? provider) where TResult : ISpanParsable<TResult>
    {
        if (typeof(T) == typeof(char))
        {
            return TResult.TryParse(GetCharSpan(), provider, out result);
        }
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseIsomorphic<TResult>(provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column using <see cref="IUtf8SpanParsable{TResult}"/> and the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseUtf8<TResult>([MaybeNullWhen(false)] out TResult result, IFormatProvider? provider = null) where TResult : IUtf8SpanParsable<TResult>
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseUtf8Isomorphic(provider ?? CultureInfo.InvariantCulture, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return Utf8SpanParser.TryParseUtf8FromChars(GetCharSpan(), provider ?? CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as an <see cref="int"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt32(out int result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return int.TryParse(GetCharSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="short"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt16(out short result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return short.TryParse(GetCharSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="uint"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseUInt32(out uint result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return uint.TryParse(GetCharSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="ushort"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseUInt16(out ushort result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return ushort.TryParse(GetCharSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="long"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt64(out long result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return long.TryParse(GetCharSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="ulong"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseUInt64(out ulong result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return ulong.TryParse(GetCharSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="double"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDouble(out double result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return double.TryParse(GetCharSpan(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="double"/> using the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDouble(out double result, IFormatProvider? provider)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseDouble(provider, NumberStyles.Float | NumberStyles.AllowThousands, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return double.TryParse(GetCharSpan(), NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="float"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseSingle(out float result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return float.TryParse(GetCharSpan(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="float"/> using the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseSingle(out float result, IFormatProvider? provider)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseSingle(provider, NumberStyles.Float | NumberStyles.AllowThousands, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return float.TryParse(GetCharSpan(), NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="decimal"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDecimal(out decimal result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return decimal.TryParse(GetCharSpan(), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="decimal"/> using the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDecimal(out decimal result, IFormatProvider? provider)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseDecimal(provider, NumberStyles.Number, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return decimal.TryParse(GetCharSpan(), NumberStyles.Number, provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="bool"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseBoolean(out bool result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return bool.TryParse(GetCharSpan(), out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="DateTime"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDateTime(out DateTime result)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseDateTime(CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return DateTime.TryParse(GetCharSpan(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTime"/> using the provided culture and styles.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseDateTime(provider, styles, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return DateTime.TryParse(GetCharSpan(), provider, styles, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTime"/> using an exact format string, culture, and styles.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseDateTimeExact(format, provider, styles, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return DateTime.TryParseExact(GetCharSpan(), format, provider, styles, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTime"/> using an exact format with invariant culture.
    /// </summary>
    public bool TryParseDateTime(out DateTime result, string format)
        => TryParseDateTime(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the column as a <see cref="DateTimeOffset"/> using invariant culture.</summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result)
        => TryParseDateTimeOffset(out result, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTimeOffset"/> using the provided culture and styles.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseDateTimeOffset(provider, styles, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return DateTimeOffset.TryParse(GetCharSpan(), provider, styles, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTimeOffset"/> using exact format, culture, and styles.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseDateTimeOffsetExact(format, provider, styles, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return DateTimeOffset.TryParseExact(GetCharSpan(), format, provider, styles, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateTimeOffset"/> using exact format with invariant culture.
    /// </summary>
    public bool TryParseDateTimeOffset(out DateTimeOffset result, string format)
        => TryParseDateTimeOffset(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the column as a <see cref="DateOnly"/> using invariant culture.</summary>
    public bool TryParseDateOnly(out DateOnly result)
        => TryParseDateOnly(out result, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the column as a <see cref="DateOnly"/> using the provided culture and styles.</summary>
    public bool TryParseDateOnly(out DateOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseDateOnly(provider, styles, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return DateOnly.TryParse(GetCharSpan(), provider, styles, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateOnly"/> using an exact format, culture, and styles.
    /// </summary>
    public bool TryParseDateOnly(out DateOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseDateOnlyExact(format, provider, styles, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return DateOnly.TryParseExact(GetCharSpan(), format, provider, styles, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="DateOnly"/> using an exact format with invariant culture.
    /// </summary>
    public bool TryParseDateOnly(out DateOnly result, string format)
        => TryParseDateOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the column as a <see cref="TimeOnly"/> using invariant culture.</summary>
    public bool TryParseTimeOnly(out TimeOnly result)
        => TryParseTimeOnly(out result, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the column as a <see cref="TimeOnly"/> using the provided culture and styles.</summary>
    public bool TryParseTimeOnly(out TimeOnly result, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseTimeOnly(provider, styles, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return TimeOnly.TryParse(GetCharSpan(), provider, styles, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="TimeOnly"/> using an exact format, culture, and styles.
    /// </summary>
    public bool TryParseTimeOnly(out TimeOnly result, string format, IFormatProvider? provider, DateTimeStyles styles = DateTimeStyles.None)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseTimeOnlyExact(format, provider, styles, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return TimeOnly.TryParseExact(GetCharSpan(), format, provider, styles, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as a <see cref="TimeOnly"/> using an exact format with invariant culture.
    /// </summary>
    public bool TryParseTimeOnly(out TimeOnly result, string format)
        => TryParseTimeOnly(out result, format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Attempts to parse the column as a <see cref="TimeZoneInfo"/> from its identifier.</summary>
    public bool TryParseTimeZoneInfo(out TimeZoneInfo result)
    {
        if (data.IsEmpty)
        {
            result = default!;
            return false;
        }

        try
        {
            string id;
            if (typeof(T) == typeof(char))
            {
                id = new string(GetCharSpan());
            }
            else if (typeof(T) == typeof(byte))
            {
                id = Decode();
            }
            else
            {
                result = default!;
                return false;
            }

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseGuid(out Guid result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return Guid.TryParse(GetCharSpan(), out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="byte"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseByte(out byte result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return byte.TryParse(GetCharSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as an <see cref="sbyte"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseSByte(out sbyte result)
    {
        if (typeof(T) == typeof(byte))
        {
            return Utf8Parser.TryParse(GetByteSpan(), out result, out int consumed) && consumed == data.Length;
        }
        if (typeof(T) == typeof(char))
        {
            return sbyte.TryParse(GetCharSpan(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse the column as an enum of type <typeparamref name="TEnum"/> using case-insensitive matching.
    /// </summary>
    public bool TryParseEnum<TEnum>(out TEnum result) where TEnum : struct, Enum
    {
        if (typeof(T) == typeof(char))
        {
            return Enum.TryParse(GetCharSpan(), ignoreCase: true, out result);
        }
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseEnum(ignoreCase: true, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="TimeSpan"/> using invariant culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result)
        => TryParseTimeSpan(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column as a <see cref="TimeSpan"/> using the specified format provider.</summary>
    public bool TryParseTimeSpan(out TimeSpan result, IFormatProvider? provider)
    {
        if (typeof(T) == typeof(char))
        {
            return TimeSpan.TryParse(GetCharSpan(), provider, out result);
        }
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseTimeSpan(provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="TimeSpan"/> using an exact format and culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result, string format, IFormatProvider? provider)
    {
        if (typeof(T) == typeof(char))
        {
            return TimeSpan.TryParseExact(GetCharSpan(), format, provider, out result);
        }
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseTimeSpanExact(format, provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="TimeSpan"/> using an exact format and invariant culture.</summary>
    public bool TryParseTimeSpan(out TimeSpan result, string format)
        => TryParseTimeSpan(out result, format, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column as a single <see cref="char"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseChar(out char result)
    {
        if (typeof(T) == typeof(char))
        {
            ReadOnlySpan<char> chars = GetCharSpan();
            if (chars.Length == 1)
            {
                result = chars[0];
                return true;
            }
            result = default;
            return false;
        }
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseChar(out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="Half"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseHalf(out Half result)
        => TryParseHalf(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column as a <see cref="Half"/> using the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseHalf(out Half result, IFormatProvider? provider)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseHalf(provider, NumberStyles.Float | NumberStyles.AllowThousands, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return Half.TryParse(GetCharSpan(), NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as an <see cref="Int128"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt128(out Int128 result)
        => TryParseInt128(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column as an <see cref="Int128"/> using the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt128(out Int128 result, IFormatProvider? provider)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseInt128(provider, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return Int128.TryParse(GetCharSpan(), NumberStyles.Integer, provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="UInt128"/> using invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseUInt128(out UInt128 result)
        => TryParseUInt128(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column as a <see cref="UInt128"/> using the specified format provider.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseUInt128(out UInt128 result, IFormatProvider? provider)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseUInt128(provider, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return UInt128.TryParse(GetCharSpan(), NumberStyles.Integer, provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse the column as a <see cref="BigInteger"/> using invariant culture.</summary>
    public bool TryParseBigInteger(out BigInteger result)
        => TryParseBigInteger(out result, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column as a <see cref="BigInteger"/> using the specified format provider.</summary>
    public bool TryParseBigInteger(out BigInteger result, IFormatProvider? provider)
    {
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseBigInteger(provider, out result);
        }
        if (typeof(T) == typeof(char))
        {
            return BigInteger.TryParse(GetCharSpan(), NumberStyles.Integer, provider, out result);
        }
        result = default;
        return false;
    }

    /// <summary>Attempts to parse a non-empty column as a <see cref="Uri"/>.</summary>
    public bool TryParseUri([NotNullWhen(true)] out Uri? result)
    {
        if (typeof(T) == typeof(char))
        {
            ReadOnlySpan<char> chars = GetCharSpan();
            if (chars.IsEmpty || chars.IsWhiteSpace())
            {
                result = null;
                return false;
            }
            return Uri.TryCreate(new string(chars.Trim()), UriKind.RelativeOrAbsolute, out result);
        }
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseUri(out result);
        }
        result = null;
        return false;
    }

    /// <summary>Attempts to parse a Base64-encoded column into a byte array.</summary>
    public bool TryParseBase64Bytes([NotNullWhen(true)] out byte[]? result)
    {
        if (typeof(T) == typeof(char))
        {
            return Utf8SpanParser.TryParseBase64Chars(GetCharSpan(), out result);
        }
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).TryParseBase64Bytes(out result);
        }
        result = null;
        return false;
    }

    /// <summary>Compares the column with a string using ordinal semantics.</summary>
    public bool Equals(string? other)
    {
        if (other is null) return false;

        if (typeof(T) == typeof(char))
        {
            return other.AsSpan().SequenceEqual(GetCharSpan());
        }
        if (typeof(T) == typeof(byte))
        {
            return new Utf8SpanParser(GetByteSpan()).EqualsString(other, StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>Returns the underlying span with surrounding quotes removed, if present.</summary>
    /// <param name="quote">Quote character (defaults to double quote).</param>
    public ReadOnlySpan<T> Unquote(T quote)
    {
        var span = data;
        if (span.Length >= 2 && span[0].Equals(quote) && span[^1].Equals(quote))
        {
            return span[1..^1];
        }
        return span;
    }

    /// <summary>Returns the underlying span with surrounding quotes removed, if present.</summary>
    public ReadOnlySpan<T> Unquote()
    {
        if (typeof(T) == typeof(char))
        {
            var quote = (T)(object)'"';
            return Unquote(quote);
        }
        if (typeof(T) == typeof(byte))
        {
            var quote = (T)(object)(byte)'"';
            return Unquote(quote);
        }
        return data;
    }

    /// <summary>Unquotes the column (if needed) and returns it as a <see cref="string"/>, collapsing doubled quotes.</summary>
    /// <param name="quote">Quote character (defaults to double quote).</param>
    public string UnquoteToString(T quote)
    {
        if (typeof(T) == typeof(char))
        {
            return UnquoteToStringChar((char)(object)quote);
        }
        if (typeof(T) == typeof(byte))
        {
            return UnquoteToStringByte((byte)(object)quote);
        }
        throw new NotSupportedException($"Element type {typeof(T)} is not supported.");
    }

    /// <summary>Unquotes the column (if needed) and returns it as a <see cref="string"/>, collapsing doubled quotes.</summary>
    public string UnquoteToString()
    {
        if (typeof(T) == typeof(char))
        {
            return UnquoteToStringChar('"');
        }
        if (typeof(T) == typeof(byte))
        {
            return UnquoteToStringByte((byte)'"');
        }
        throw new NotSupportedException($"Element type {typeof(T)} is not supported.");
    }

    /// <summary>
    /// Unquotes and unescapes the column, handling both doubled quotes (RFC 4180) and escape sequences.
    /// </summary>
    /// <param name="quote">Quote character (defaults to double quote).</param>
    /// <param name="escape">Optional escape character (e.g., backslash). When specified, escape sequences like \" are processed.</param>
    public string UnquoteToString(T quote, T? escape)
    {
        if (typeof(T) == typeof(char))
        {
            return UnquoteToStringCharWithEscape((char)(object)quote, escape is null ? null : (char)(object)escape);
        }
        if (typeof(T) == typeof(byte))
        {
            var byteQuote = (byte)(object)quote;
            var byteEscape = escape is null ? (byte?)null : (byte)(object)escape;
            return UnquoteToStringByteWithEscape(byteQuote, byteEscape);
        }
        throw new NotSupportedException($"Element type {typeof(T)} is not supported.");
    }

    // ========== Private helper methods ==========

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<char> GetCharSpan() =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, char>(ref MemoryMarshal.GetReference(data)), data.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetByteSpan() =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(data)), data.Length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string Decode() => Encoding.UTF8.GetString(GetByteSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string UnquoteToStringChar(char quote)
        => new CsvPipeColumnText(quote, escape: null, enableQuotedFields: true).Decode(GetCharSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string UnquoteToStringByte(byte quote)
        => new CsvPipeColumnText(quote, escape: null, enableQuotedFields: true).Decode(GetByteSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string UnquoteToStringByteWithEscape(byte quote, byte? escape)
        => new CsvPipeColumnText(quote, escape, enableQuotedFields: true).Decode(GetByteSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string UnquoteToStringCharWithEscape(char quote, char? escape)
        => new CsvPipeColumnText(quote, escape, enableQuotedFields: true).Decode(GetCharSpan());
}
