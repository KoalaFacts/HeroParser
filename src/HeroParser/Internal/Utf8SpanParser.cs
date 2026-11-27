using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;

namespace HeroParser.Internal;

/// <summary>
/// Shared zero-allocation UTF-8 span parsing primitives for columns and internal binders.
/// Uses <see cref="Utf8Parser"/> on invariant fast paths and stackalloc/pooled char buffers otherwise.
/// </summary>
internal readonly ref struct Utf8SpanParser(ReadOnlySpan<byte> utf8)
{
    private const int STACK_ALLOC_CHAR_LIMIT = 256;
    internal const string EMPTY_BASE64_TOKEN = "base64:";
    private static ReadOnlySpan<byte> EmptyBase64TokenUtf8 => "base64:"u8;
    internal const string CHAR_TOKEN_PREFIX = "char:";
    private static ReadOnlySpan<byte> CharTokenPrefixUtf8 => "char:"u8;
    private readonly ReadOnlySpan<byte> utf8 = utf8;

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

    /// <summary>Decodes the UTF-8 span to a managed string.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal string Decode() => Encoding.UTF8.GetString(utf8);

    /// <summary>
    /// Parses the UTF-8 span using <see cref="ISpanParsable{T}"/> without allocating on typical field sizes.
    /// </summary>
    internal T ParseIsomorphic<T>(IFormatProvider? provider)
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
    /// Attempts to parse the UTF-8 span using <see cref="ISpanParsable{T}"/> without allocating on typical field sizes.
    /// </summary>
    internal bool TryParseIsomorphic<T>(
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
    /// Checks whether the UTF-8 byte span is empty or contains only whitespace characters without heap allocation.
    /// </summary>
    internal bool IsNullOrWhiteSpace()
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

    /// <summary>Attempts to parse the UTF-8 span as an <see cref="int"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseInt32(IFormatProvider? provider, out int result)
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
            static (chars, p, out parsed) => new CultureNumberParser(p).TryParse(chars, NumberStyles.Integer, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="long"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseInt64(IFormatProvider? provider, out long result)
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
            static (chars, p, out parsed) => new CultureNumberParser(p).TryParse(chars, NumberStyles.Integer, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="short"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseInt16(IFormatProvider? provider, out short result)
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
            static (chars, p, out parsed) => new CultureNumberParser(p).TryParse(chars, NumberStyles.Integer, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="byte"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseByte(IFormatProvider? provider, out byte result)
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
            static (chars, p, out parsed) => new CultureNumberParser(p).TryParse(chars, NumberStyles.Integer, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="uint"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseUInt32(IFormatProvider? provider, out uint result)
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
            static (chars, p, out parsed) => uint.TryParse(chars, NumberStyles.Integer, p, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="ulong"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseUInt64(IFormatProvider? provider, out ulong result)
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
            static (chars, p, out parsed) => ulong.TryParse(chars, NumberStyles.Integer, p, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="ushort"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseUInt16(IFormatProvider? provider, out ushort result)
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
            static (chars, p, out parsed) => ushort.TryParse(chars, NumberStyles.Integer, p, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as an <see cref="sbyte"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseSByte(IFormatProvider? provider, out sbyte result)
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
            static (chars, p, out parsed) => new CultureNumberParser(p).TryParse(chars, NumberStyles.Integer, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="decimal"/> using the specified culture and styles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseDecimal(IFormatProvider? provider, NumberStyles styles, out decimal result)
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
            static (chars, state, out parsed) => new CultureNumberParser(state.Provider).TryParse(chars, state.Styles, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="double"/> using the specified culture and styles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseDouble(IFormatProvider? provider, NumberStyles styles, out double result)
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
            static (chars, state, out parsed) => new CultureNumberParser(state.Provider).TryParse(chars, state.Styles, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="float"/> using the specified culture and styles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseSingle(IFormatProvider? provider, NumberStyles styles, out float result)
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
            static (chars, state, out parsed) => new CultureNumberParser(state.Provider).TryParse(chars, state.Styles, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="bool"/> supporting 1/0, Y/N, T/F, and standard boolean literals.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseBoolean(out bool result)
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

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="Guid"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseGuid(out Guid result)
    {
        if (Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length)
            return true;

        return TryParseDecoded(
            utf8,
            0,
            static (chars, _, out parsed) => Guid.TryParse(chars, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="DateTime"/>.</summary>
    internal bool TryParseDateTime(IFormatProvider? provider, DateTimeStyles styles, out DateTime result)
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

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="DateTime"/> using an exact format.</summary>
    internal bool TryParseDateTimeExact(
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

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="DateTimeOffset"/>.</summary>
    internal bool TryParseDateTimeOffset(
        IFormatProvider? provider,
        DateTimeStyles styles,
        out DateTimeOffset result)
        => TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => DateTimeOffset.TryParse(chars, state.Provider, state.Styles, out parsed),
            out result);

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="DateTimeOffset"/> using an exact format.</summary>
    internal bool TryParseDateTimeOffsetExact(
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

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="DateOnly"/>.</summary>
    internal bool TryParseDateOnly(
        IFormatProvider? provider,
        DateTimeStyles styles,
        out DateOnly result)
        => TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => DateOnly.TryParse(chars, state.Provider, state.Styles, out parsed),
            out result);

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="DateOnly"/> using an exact format.</summary>
    internal bool TryParseDateOnlyExact(
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

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="TimeOnly"/>.</summary>
    internal bool TryParseTimeOnly(
        IFormatProvider? provider,
        DateTimeStyles styles,
        out TimeOnly result)
        => TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => TimeOnly.TryParse(chars, state.Provider, state.Styles, out parsed),
            out result);

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="TimeOnly"/> using an exact format.</summary>
    internal bool TryParseTimeOnlyExact(
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

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="TimeSpan"/>.</summary>
    internal bool TryParseTimeSpan(IFormatProvider? provider, out TimeSpan result)
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

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="TimeSpan"/> using an exact format.</summary>
    internal bool TryParseTimeSpanExact(
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

    /// <summary>Attempts to parse a char span representing either a single character or a <c>char:&lt;c&gt;</c> token.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryParseCharSpan(ReadOnlySpan<char> chars, out char result)
    {
        if (chars.Length == 1)
        {
            result = chars[0];
            return true;
        }

        if (chars.Length == CHAR_TOKEN_PREFIX.Length + 1 && chars.StartsWith(CHAR_TOKEN_PREFIX.AsSpan(), StringComparison.Ordinal))
        {
            result = chars[^1];
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>Attempts to parse the UTF-8 span representing a single UTF-16 character.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseChar(out char result)
    {
        if (utf8.IsEmpty)
        {
            result = default;
            return false;
        }

        if (utf8.Length == 1 && utf8[0] < 0x80)
        {
            result = (char)utf8[0];
            return true;
        }

        ReadOnlySpan<byte> payload = utf8.Length > CharTokenPrefixUtf8.Length && utf8.StartsWith(CharTokenPrefixUtf8)
            ? utf8[CharTokenPrefixUtf8.Length..]
            : utf8;

        if (payload.Length == 1 && payload[0] < 0x80)
        {
            result = (char)payload[0];
            return true;
        }

        if (payload.Length <= 4)
        {
            Span<char> chars = stackalloc char[4];
            if (Utf8.ToUtf16(payload, chars, out _, out int charsWritten, replaceInvalidSequences: false) == OperationStatus.Done &&
                charsWritten == 1)
            {
                result = chars[0];
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="Half"/> using the specified culture and styles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseHalf(IFormatProvider? provider, NumberStyles styles, out Half result)
    {
        if (Half.TryParse(utf8, styles, provider, out result))
            return true;

        return !IsInvariant(provider) && TryParseDecoded(
            utf8,
            (Styles: styles, Provider: provider),
            static (chars, state, out parsed) => new CultureNumberParser(state.Provider).TryParse(chars, state.Styles, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as an <see cref="Int128"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseInt128(IFormatProvider? provider, out Int128 result)
    {
        if (Int128.TryParse(utf8, NumberStyles.Integer, provider, out result))
            return true;

        return !IsInvariant(provider) && TryParseDecoded(
            utf8,
            provider,
            static (chars, p, out parsed) => new CultureNumberParser(p).TryParse(chars, NumberStyles.Integer, out parsed),
            out result);
    }

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="UInt128"/> using the specified culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseUInt128(IFormatProvider? provider, out UInt128 result)
        => UInt128.TryParse(utf8, NumberStyles.Integer, provider, out result);

    /// <summary>Attempts to parse the UTF-8 span as a <see cref="BigInteger"/> using the specified culture.</summary>
    internal bool TryParseBigInteger(IFormatProvider? provider, out BigInteger result)
        => TryParseDecoded(
            utf8,
            provider,
            static (chars, p, out parsed) => new CultureNumberParser(p).TryParse(chars, NumberStyles.Integer, out parsed),
            out result);

    /// <summary>Attempts to parse a non-empty UTF-8 span as a <see cref="Uri"/>.</summary>
    internal bool TryParseUri([NotNullWhen(true)] out Uri? result)
    {
        if (IsNullOrWhiteSpace())
        {
            result = null;
            return false;
        }

        string decoded = Decode().Trim();
        return Uri.TryCreate(decoded, UriKind.RelativeOrAbsolute, out result);
    }

    /// <summary>Attempts to parse the UTF-8 Base64-encoded span into a byte array.</summary>
    internal bool TryParseBase64Bytes([NotNullWhen(true)] out byte[]? result)
    {
        ReadOnlySpan<byte> trimmed = TrimAsciiWhiteSpace(utf8);
        if (trimmed.StartsWith(EmptyBase64TokenUtf8))
            trimmed = trimmed[EmptyBase64TokenUtf8.Length..];

        if (trimmed.IsEmpty)
        {
            result = [];
            return true;
        }

        int maxDecoded = Base64.GetMaxDecodedFromUtf8Length(trimmed.Length);
        byte[]? rented = maxDecoded > 1024 ? ArrayPool<byte>.Shared.Rent(maxDecoded) : null;
        Span<byte> buffer = rented is not null ? rented : stackalloc byte[maxDecoded];
        try
        {
            if (Base64.DecodeFromUtf8(trimmed, buffer, out _, out int bytesWritten) == OperationStatus.Done)
            {
                result = buffer[..bytesWritten].ToArray();
                return true;
            }

            result = null;
            return false;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Attempts to parse a char Base64-encoded span into a byte array.</summary>
    internal static bool TryParseBase64Chars(ReadOnlySpan<char> chars, [NotNullWhen(true)] out byte[]? result)
    {
        ReadOnlySpan<char> trimmed = chars.Trim();
        if (trimmed.StartsWith(EMPTY_BASE64_TOKEN.AsSpan(), StringComparison.Ordinal))
            trimmed = trimmed[EMPTY_BASE64_TOKEN.Length..];

        if (trimmed.IsEmpty)
        {
            result = [];
            return true;
        }

        int maxDecoded = (trimmed.Length * 3 + 3) / 4;
        byte[]? rented = maxDecoded > 1024 ? ArrayPool<byte>.Shared.Rent(maxDecoded) : null;
        Span<byte> buffer = rented is not null ? rented : stackalloc byte[maxDecoded];
        try
        {
            if (Convert.TryFromBase64Chars(trimmed, buffer, out int bytesWritten))
            {
                result = buffer[..bytesWritten].ToArray();
                return true;
            }

            result = null;
            return false;
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Attempts to parse the UTF-8 span directly using <see cref="IUtf8SpanParsable{T}"/> without intermediate UTF-16 conversion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryParseUtf8Isomorphic<T>(
        IFormatProvider? provider,
        [MaybeNullWhen(false)] out T result)
        where T : IUtf8SpanParsable<T>
        => T.TryParse(utf8, provider, out result);

    /// <summary>
    /// Attempts to parse a UTF-16 char span using <see cref="IUtf8SpanParsable{T}"/> without allocating on typical field sizes.
    /// </summary>
    internal static bool TryParseUtf8FromChars<T>(
        ReadOnlySpan<char> chars,
        IFormatProvider? provider,
        [MaybeNullWhen(false)] out T result)
        where T : IUtf8SpanParsable<T>
    {
        if (chars.IsEmpty)
            return T.TryParse([], provider, out result);

        int maxBytes = Encoding.UTF8.GetMaxByteCount(chars.Length);
        if (maxBytes <= STACK_ALLOC_CHAR_LIMIT * 2)
        {
            Span<byte> stackBytes = stackalloc byte[maxBytes];
            int written = Encoding.UTF8.GetBytes(chars, stackBytes);
            return T.TryParse(stackBytes[..written], provider, out result);
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            int written = Encoding.UTF8.GetBytes(chars, rented);
            return T.TryParse(rented.AsSpan(0, written), provider, out result);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Attempts to parse the UTF-8 span as an enum of type <typeparamref name="TEnum"/>.</summary>
    internal bool TryParseEnum<TEnum>(bool ignoreCase, out TEnum result)
        where TEnum : struct, Enum
        => TryParseDecoded(
            utf8,
            ignoreCase,
            static (chars, ic, out parsed) => Enum.TryParse(chars, ic, out parsed),
            out result);

    /// <summary>Compares the UTF-8 byte span with a UTF-16 string without heap allocation on typical field sizes.</summary>
    internal bool EqualsString(
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> TrimAsciiWhiteSpace(ReadOnlySpan<byte> span)
    {
        int start = 0;
        while (start < span.Length && IsAsciiWhiteSpace(span[start]))
            start++;
        int end = span.Length - 1;
        while (end >= start && IsAsciiWhiteSpace(span[end]))
            end--;
        return span[start..(end + 1)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiWhiteSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\v' or (byte)'\f';

    private static bool TryParseDecoded<TState, TResult>(
        ReadOnlySpan<byte> span,
        TState state,
        DecodedCharParser<TState, TResult> parser,
        out TResult result)
    {
        if (span.IsEmpty)
            return parser([], state, out result);

        int maxChars = Encoding.UTF8.GetMaxCharCount(span.Length);
        if (maxChars <= STACK_ALLOC_CHAR_LIMIT)
        {
            Span<char> chars = stackalloc char[maxChars];
            int written = Encoding.UTF8.GetChars(span, chars);
            return parser(chars[..written], state, out result);
        }

        char[] rented = ArrayPool<char>.Shared.Rent(maxChars);
        try
        {
            int written = Encoding.UTF8.GetChars(span, rented);
            return parser(rented.AsSpan(0, written), state, out result);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static bool TryParseDecodedExact<TStyle, TResult>(
        ReadOnlySpan<byte> span,
        ReadOnlySpan<char> format,
        IFormatProvider? provider,
        TStyle styles,
        DecodedFormattedCharParser<TStyle, TResult> parser,
        out TResult result)
    {
        if (span.IsEmpty)
            return parser([], format, provider, styles, out result);

        int maxChars = Encoding.UTF8.GetMaxCharCount(span.Length);
        if (maxChars <= STACK_ALLOC_CHAR_LIMIT)
        {
            Span<char> chars = stackalloc char[maxChars];
            int written = Encoding.UTF8.GetChars(span, chars);
            return parser(chars[..written], format, provider, styles, out result);
        }

        char[] rented = ArrayPool<char>.Shared.Rent(maxChars);
        try
        {
            int written = Encoding.UTF8.GetChars(span, rented);
            return parser(rented.AsSpan(0, written), format, provider, styles, out result);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static bool IsNonAsciiWhiteSpace(ReadOnlySpan<byte> span)
    {
        int maxChars = Encoding.UTF8.GetMaxCharCount(span.Length);
        if (maxChars <= STACK_ALLOC_CHAR_LIMIT)
        {
            Span<char> chars = stackalloc char[maxChars];
            int written = Encoding.UTF8.GetChars(span, chars);
            return chars[..written].IsWhiteSpace();
        }

        char[] rented = ArrayPool<char>.Shared.Rent(maxChars);
        try
        {
            int written = Encoding.UTF8.GetChars(span, rented);
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
