using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace HeroParser.Internal;

/// <summary>
/// Shared zero-allocation UTF-8 span parsing primitives for columns and internal binders.
/// Uses <see cref="Utf8Parser"/> on invariant fast paths and stackalloc/pooled char buffers otherwise.
/// </summary>
internal static class Utf8SpanParser
{
    private const int STACK_ALLOC_CHAR_LIMIT = 256;

    private delegate bool DecodedCharParser<TState, TResult>(ReadOnlySpan<char> chars, TState state, out TResult result);

    private delegate bool DecodedFormattedCharParser<TStyle, TResult>(
        ReadOnlySpan<char> chars,
        ReadOnlySpan<char> format,
        IFormatProvider? provider,
        TStyle styles,
        out TResult result);

    /// <summary>Returns <see langword="true"/> when the encoding matches UTF-8.</summary>
    internal static bool IsUtf8Encoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.CodePage == Encoding.UTF8.CodePage;
    }

    /// <summary>Decodes a UTF-8 span to a managed string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string Decode(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetString(utf8);

    /// <summary>
    /// Parses a UTF-8 span using <see cref="ISpanParsable{T}"/> without allocating on typical field sizes.
    /// </summary>
    internal static T ParseIsomorphic<T>(ReadOnlySpan<byte> utf8, IFormatProvider? provider)
        where T : ISpanParsable<T>
    {
        if (utf8.IsEmpty)
            return T.Parse([], provider);

        int maxChars = Encoding.UTF8.GetMaxCharCount(utf8.Length);
        if (maxChars <= STACK_ALLOC_CHAR_LIMIT)
        {
            Span<char> chars = stackalloc char[maxChars];
            int written = Encoding.UTF8.GetChars(utf8, chars);
            return T.Parse(chars[..written], provider);
        }

        char[] rented = ArrayPool<char>.Shared.Rent(maxChars);
        try
        {
            int written = Encoding.UTF8.GetChars(utf8, rented);
            return T.Parse(rented.AsSpan(0, written), provider);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Attempts to parse a UTF-8 span using <see cref="ISpanParsable{T}"/> without allocating on typical field sizes.
    /// </summary>
    internal static bool TryParseIsomorphic<T>(
        ReadOnlySpan<byte> utf8,
        IFormatProvider? provider,
        [MaybeNullWhen(false)] out T result)
        where T : ISpanParsable<T>
    {
        if (utf8.IsEmpty)
            return T.TryParse([], provider, out result);

        int maxChars = Encoding.UTF8.GetMaxCharCount(utf8.Length);
        if (maxChars <= STACK_ALLOC_CHAR_LIMIT)
        {
            Span<char> chars = stackalloc char[maxChars];
            int written = Encoding.UTF8.GetChars(utf8, chars);
            return T.TryParse(chars[..written], provider, out result);
        }

        char[] rented = ArrayPool<char>.Shared.Rent(maxChars);
        try
        {
            int written = Encoding.UTF8.GetChars(utf8, rented);
            return T.TryParse(rented.AsSpan(0, written), provider, out result);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Checks whether a UTF-8 byte span is empty or contains only whitespace characters without heap allocation.
    /// </summary>
    internal static bool IsNullOrWhiteSpace(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
            return true;

        foreach (byte value in utf8)
        {
            if (value > 0x7F)
                return IsNonAsciiWhiteSpace(utf8);

            if (!char.IsWhiteSpace((char)value))
                return false;
        }

        return true;
    }

    /// <summary>Attempts to parse a UTF-8 span as an <see cref="int"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseInt32(ReadOnlySpan<byte> utf8, IFormatProvider? provider, out int result)
    {
        if (IsInvariant(provider) &&
            Utf8Parser.TryParse(utf8, out result, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseDecoded(
            utf8,
            provider,
            static (chars, p, out parsed) => CultureNumberParser.TryParse(chars, NumberStyles.Integer, p, out parsed),
            out result);
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="long"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseInt64(ReadOnlySpan<byte> utf8, IFormatProvider? provider, out long result)
    {
        if (IsInvariant(provider) &&
            Utf8Parser.TryParse(utf8, out result, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseDecoded(
            utf8,
            provider,
            static (chars, p, out parsed) => CultureNumberParser.TryParse(chars, NumberStyles.Integer, p, out parsed),
            out result);
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="short"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseInt16(ReadOnlySpan<byte> utf8, IFormatProvider? provider, out short result)
    {
        if (IsInvariant(provider) &&
            Utf8Parser.TryParse(utf8, out result, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseDecoded(
            utf8,
            provider,
            static (chars, p, out parsed) => CultureNumberParser.TryParse(chars, NumberStyles.Integer, p, out parsed),
            out result);
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="byte"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseByte(ReadOnlySpan<byte> utf8, IFormatProvider? provider, out byte result)
    {
        if (IsInvariant(provider) &&
            Utf8Parser.TryParse(utf8, out result, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseDecoded(
            utf8,
            provider,
            static (chars, p, out parsed) => CultureNumberParser.TryParse(chars, NumberStyles.Integer, p, out parsed),
            out result);
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="decimal"/> using the specified culture and styles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseDecimal(ReadOnlySpan<byte> utf8, IFormatProvider? provider, NumberStyles styles, out decimal result)
    {
        if (IsInvariant(provider) &&
            styles == NumberStyles.Number &&
            Utf8Parser.TryParse(utf8, out result, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => CultureNumberParser.TryParse(chars, state.Styles, state.Provider, out parsed),
            out result);
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="double"/> using the specified culture and styles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseDouble(ReadOnlySpan<byte> utf8, IFormatProvider? provider, NumberStyles styles, out double result)
    {
        if (IsInvariant(provider) &&
            styles == (NumberStyles.Float | NumberStyles.AllowThousands) &&
            Utf8Parser.TryParse(utf8, out result, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => CultureNumberParser.TryParse(chars, state.Styles, state.Provider, out parsed),
            out result);
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="float"/> using the specified culture and styles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseSingle(ReadOnlySpan<byte> utf8, IFormatProvider? provider, NumberStyles styles, out float result)
    {
        if (IsInvariant(provider) &&
            styles == (NumberStyles.Float | NumberStyles.AllowThousands) &&
            Utf8Parser.TryParse(utf8, out result, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => CultureNumberParser.TryParse(chars, state.Styles, state.Provider, out parsed),
            out result);
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="bool"/> supporting 1/0, Y/N, T/F, and standard boolean literals.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseBoolean(ReadOnlySpan<byte> utf8, out bool result)
    {
        if (utf8.Length == 1)
        {
#pragma warning disable IDE0010 // Populate switch - intentionally not exhaustive
            switch (utf8[0])
            {
                case (byte)'1':
                case (byte)'Y':
                case (byte)'y':
                case (byte)'T':
                case (byte)'t':
                    result = true;
                    return true;

                case (byte)'0':
                case (byte)'N':
                case (byte)'n':
                case (byte)'F':
                case (byte)'f':
                    result = false;
                    return true;
            }
#pragma warning restore IDE0010
        }

        if (Utf8Parser.TryParse(utf8, out bool parsed, out int consumed) &&
            consumed == utf8.Length)
        {
            result = parsed;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="Guid"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseGuid(ReadOnlySpan<byte> utf8, out Guid result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="DateTime"/>.</summary>
    internal static bool TryParseDateTime(ReadOnlySpan<byte> utf8, IFormatProvider? provider, DateTimeStyles styles, out DateTime result)
    {
        if (IsInvariant(provider) &&
            styles == DateTimeStyles.None &&
            Utf8Parser.TryParse(utf8, out result, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => DateTime.TryParse(chars, state.Provider, state.Styles, out parsed),
            out result);
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="DateTime"/> using an exact format.</summary>
    internal static bool TryParseDateTimeExact(
        ReadOnlySpan<byte> utf8,
        ReadOnlySpan<char> format,
        IFormatProvider? provider,
        DateTimeStyles styles,
        out DateTime result)
        => TryParseDecodedExact(
            utf8,
            format,
            provider,
            styles,
            static (chars, fmt, p, st, out parsed) => DateTime.TryParseExact(chars, fmt, p, st, out parsed),
            out result);

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="DateTimeOffset"/>.</summary>
    internal static bool TryParseDateTimeOffset(
        ReadOnlySpan<byte> utf8,
        IFormatProvider? provider,
        DateTimeStyles styles,
        out DateTimeOffset result)
        => TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => DateTimeOffset.TryParse(chars, state.Provider, state.Styles, out parsed),
            out result);

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="DateTimeOffset"/> using an exact format.</summary>
    internal static bool TryParseDateTimeOffsetExact(
        ReadOnlySpan<byte> utf8,
        ReadOnlySpan<char> format,
        IFormatProvider? provider,
        DateTimeStyles styles,
        out DateTimeOffset result)
        => TryParseDecodedExact(
            utf8,
            format,
            provider,
            styles,
            static (chars, fmt, p, st, out parsed) => DateTimeOffset.TryParseExact(chars, fmt, p, st, out parsed),
            out result);

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="DateOnly"/>.</summary>
    internal static bool TryParseDateOnly(
        ReadOnlySpan<byte> utf8,
        IFormatProvider? provider,
        DateTimeStyles styles,
        out DateOnly result)
        => TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => DateOnly.TryParse(chars, state.Provider, state.Styles, out parsed),
            out result);

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="DateOnly"/> using an exact format.</summary>
    internal static bool TryParseDateOnlyExact(
        ReadOnlySpan<byte> utf8,
        ReadOnlySpan<char> format,
        IFormatProvider? provider,
        DateTimeStyles styles,
        out DateOnly result)
        => TryParseDecodedExact(
            utf8,
            format,
            provider,
            styles,
            static (chars, fmt, p, st, out parsed) => DateOnly.TryParseExact(chars, fmt, p, st, out parsed),
            out result);

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="TimeOnly"/>.</summary>
    internal static bool TryParseTimeOnly(
        ReadOnlySpan<byte> utf8,
        IFormatProvider? provider,
        DateTimeStyles styles,
        out TimeOnly result)
        => TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => TimeOnly.TryParse(chars, state.Provider, state.Styles, out parsed),
            out result);

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="TimeOnly"/> using an exact format.</summary>
    internal static bool TryParseTimeOnlyExact(
        ReadOnlySpan<byte> utf8,
        ReadOnlySpan<char> format,
        IFormatProvider? provider,
        DateTimeStyles styles,
        out TimeOnly result)
        => TryParseDecodedExact(
            utf8,
            format,
            provider,
            styles,
            static (chars, fmt, p, st, out parsed) => TimeOnly.TryParseExact(chars, fmt, p, st, out parsed),
            out result);

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="TimeSpan"/>.</summary>
    internal static bool TryParseTimeSpan(ReadOnlySpan<byte> utf8, IFormatProvider? provider, out TimeSpan result)
    {
        if (IsInvariant(provider) &&
            Utf8Parser.TryParse(utf8, out result, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseDecoded(
            utf8,
            provider,
            static (chars, p, out parsed) => TimeSpan.TryParse(chars, p, out parsed),
            out result);
    }

    /// <summary>Attempts to parse a UTF-8 span as a <see cref="TimeSpan"/> using an exact format.</summary>
    internal static bool TryParseTimeSpanExact(
        ReadOnlySpan<byte> utf8,
        ReadOnlySpan<char> format,
        IFormatProvider? provider,
        out TimeSpan result)
        => TryParseDecodedExact(
            utf8,
            format,
            provider,
            0,
            static (chars, fmt, p, _, out parsed) => TimeSpan.TryParseExact(chars, fmt, p, out parsed),
            out result);

    /// <summary>Attempts to parse a UTF-8 span as an enum of type <typeparamref name="TEnum"/>.</summary>
    internal static bool TryParseEnum<TEnum>(ReadOnlySpan<byte> utf8, bool ignoreCase, out TEnum result)
        where TEnum : struct, Enum
        => TryParseDecoded(
            utf8,
            ignoreCase,
            static (chars, ic, out parsed) => Enum.TryParse(chars, ic, out parsed),
            out result);

    /// <summary>Compares a UTF-8 byte span with a UTF-16 string without heap allocation on typical field sizes.</summary>
    internal static bool EqualsString(
        ReadOnlySpan<byte> utf8,
        string? other,
        StringComparison comparison = StringComparison.Ordinal)
    {
        if (other is null)
            return false;

        if (utf8.IsEmpty)
            return other.Length == 0;

        if (comparison == StringComparison.Ordinal &&
            Encoding.UTF8.GetMaxCharCount(utf8.Length) < other.Length)
        {
            return false;
        }

        int maxChars = Encoding.UTF8.GetMaxCharCount(utf8.Length);
        if (maxChars <= STACK_ALLOC_CHAR_LIMIT)
        {
            Span<char> chars = stackalloc char[maxChars];
            int written = Encoding.UTF8.GetChars(utf8, chars);
            return chars[..written].Equals(other.AsSpan(), comparison);
        }

        char[] rented = ArrayPool<char>.Shared.Rent(maxChars);
        try
        {
            int written = Encoding.UTF8.GetChars(utf8, rented);
            return rented.AsSpan(0, written).Equals(other.AsSpan(), comparison);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static bool TryParseDecoded<TState, TResult>(
        ReadOnlySpan<byte> utf8,
        TState state,
        DecodedCharParser<TState, TResult> parser,
        out TResult result)
    {
        if (utf8.IsEmpty)
            return parser([], state, out result);

        int maxChars = Encoding.UTF8.GetMaxCharCount(utf8.Length);
        if (maxChars <= STACK_ALLOC_CHAR_LIMIT)
        {
            Span<char> chars = stackalloc char[maxChars];
            int written = Encoding.UTF8.GetChars(utf8, chars);
            return parser(chars[..written], state, out result);
        }

        char[] rented = ArrayPool<char>.Shared.Rent(maxChars);
        try
        {
            int written = Encoding.UTF8.GetChars(utf8, rented);
            return parser(rented.AsSpan(0, written), state, out result);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static bool TryParseDecodedExact<TStyle, TResult>(
        ReadOnlySpan<byte> utf8,
        ReadOnlySpan<char> format,
        IFormatProvider? provider,
        TStyle styles,
        DecodedFormattedCharParser<TStyle, TResult> parser,
        out TResult result)
    {
        if (utf8.IsEmpty)
            return parser([], format, provider, styles, out result);

        int maxChars = Encoding.UTF8.GetMaxCharCount(utf8.Length);
        if (maxChars <= STACK_ALLOC_CHAR_LIMIT)
        {
            Span<char> chars = stackalloc char[maxChars];
            int written = Encoding.UTF8.GetChars(utf8, chars);
            return parser(chars[..written], format, provider, styles, out result);
        }

        char[] rented = ArrayPool<char>.Shared.Rent(maxChars);
        try
        {
            int written = Encoding.UTF8.GetChars(utf8, rented);
            return parser(rented.AsSpan(0, written), format, provider, styles, out result);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static bool IsNonAsciiWhiteSpace(ReadOnlySpan<byte> utf8)
    {
        int maxChars = Encoding.UTF8.GetMaxCharCount(utf8.Length);
        if (maxChars <= STACK_ALLOC_CHAR_LIMIT)
        {
            Span<char> chars = stackalloc char[maxChars];
            int written = Encoding.UTF8.GetChars(utf8, chars);
            return chars[..written].IsWhiteSpace();
        }

        char[] rented = ArrayPool<char>.Shared.Rent(maxChars);
        try
        {
            int written = Encoding.UTF8.GetChars(utf8, rented);
            return rented.AsSpan(0, written).IsWhiteSpace();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInvariant(IFormatProvider? provider)
        => provider is null || Equals(provider, CultureInfo.InvariantCulture);
}
