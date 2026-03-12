using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text;

#pragma warning disable CS1591
#pragma warning disable IDE0350

namespace HeroParser.SeparatedValues.Reading.Binders;

/// <summary>
/// UTF-8 parsing helpers used by source-generated CSV binders.
/// </summary>
internal static class CsvUtf8BindingHelper
{
    private const int STACKALLOC_CHAR_THRESHOLD = 256;

    /// <summary>
    /// Decodes a UTF-8 span to a managed string.
    /// </summary>
    internal static string Decode(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetString(utf8);

    internal static bool TryParseInt32(ReadOnlySpan<byte> utf8, CultureInfo culture, out int value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out int parsed) =>
                int.TryParse(chars, NumberStyles.Integer, provider, out parsed),
            out value);
    }

    internal static bool TryParseInt64(ReadOnlySpan<byte> utf8, CultureInfo culture, out long value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out long parsed) =>
                long.TryParse(chars, NumberStyles.Integer, provider, out parsed),
            out value);
    }

    internal static bool TryParseInt16(ReadOnlySpan<byte> utf8, CultureInfo culture, out short value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out short parsed) =>
                short.TryParse(chars, NumberStyles.Integer, provider, out parsed),
            out value);
    }

    internal static bool TryParseByte(ReadOnlySpan<byte> utf8, CultureInfo culture, out byte value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out byte parsed) =>
                byte.TryParse(chars, NumberStyles.Integer, provider, out parsed),
            out value);
    }

    internal static bool TryParseUInt32(ReadOnlySpan<byte> utf8, CultureInfo culture, out uint value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out uint parsed) =>
                uint.TryParse(chars, NumberStyles.Integer, provider, out parsed),
            out value);
    }

    internal static bool TryParseUInt64(ReadOnlySpan<byte> utf8, CultureInfo culture, out ulong value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out ulong parsed) =>
                ulong.TryParse(chars, NumberStyles.Integer, provider, out parsed),
            out value);
    }

    internal static bool TryParseUInt16(ReadOnlySpan<byte> utf8, CultureInfo culture, out ushort value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out ushort parsed) =>
                ushort.TryParse(chars, NumberStyles.Integer, provider, out parsed),
            out value);
    }

    internal static bool TryParseSByte(ReadOnlySpan<byte> utf8, CultureInfo culture, out sbyte value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out sbyte parsed) =>
                sbyte.TryParse(chars, NumberStyles.Integer, provider, out parsed),
            out value);
    }

    internal static bool TryParseDecimal(ReadOnlySpan<byte> utf8, CultureInfo culture, out decimal value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out decimal parsed) =>
                decimal.TryParse(chars, NumberStyles.Number, provider, out parsed),
            out value);
    }

    internal static bool TryParseDouble(ReadOnlySpan<byte> utf8, CultureInfo culture, out double value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out double parsed) =>
                double.TryParse(chars, NumberStyles.Float | NumberStyles.AllowThousands, provider, out parsed),
            out value);
    }

    internal static bool TryParseSingle(ReadOnlySpan<byte> utf8, CultureInfo culture, out float value)
    {
        if (IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        return TryParseChars(
            utf8,
            culture,
            static (ReadOnlySpan<char> chars, CultureInfo provider, out float parsed) =>
                float.TryParse(chars, NumberStyles.Float | NumberStyles.AllowThousands, provider, out parsed),
            out value);
    }

    internal static bool TryParseBoolean(ReadOnlySpan<byte> utf8, out bool value)
        => Utf8Parser.TryParse(utf8, out value, out int consumed) && consumed == utf8.Length;

    internal static bool TryParseGuid(ReadOnlySpan<byte> utf8, out Guid value)
        => Utf8Parser.TryParse(utf8, out value, out int consumed) && consumed == utf8.Length;

    internal static bool TryParseDateTime(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out DateTime value)
    {
        if (format is null &&
            IsInvariantCulture(culture) &&
            Utf8Parser.TryParse(utf8, out value, out int consumed) &&
            consumed == utf8.Length)
        {
            return true;
        }

        if (format is null)
        {
            return TryParseChars(
                utf8,
                culture,
                static (ReadOnlySpan<char> chars, CultureInfo provider, out DateTime parsed) =>
                    DateTime.TryParse(chars, provider, DateTimeStyles.None, out parsed),
                out value);
        }

        return TryParseChars(
            utf8,
            culture,
            format,
            static (ReadOnlySpan<char> chars, CultureInfo provider, string exactFormat, out DateTime parsed) =>
                DateTime.TryParseExact(chars, exactFormat, provider, DateTimeStyles.None, out parsed),
            out value);
    }

    internal static bool TryParseDateTimeOffset(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out DateTimeOffset value)
    {
        if (format is null)
        {
            return TryParseChars(
                utf8,
                culture,
                static (ReadOnlySpan<char> chars, CultureInfo provider, out DateTimeOffset parsed) =>
                    DateTimeOffset.TryParse(chars, provider, DateTimeStyles.None, out parsed),
                out value);
        }

        return TryParseChars(
            utf8,
            culture,
            format,
            static (ReadOnlySpan<char> chars, CultureInfo provider, string exactFormat, out DateTimeOffset parsed) =>
                DateTimeOffset.TryParseExact(chars, exactFormat, provider, DateTimeStyles.None, out parsed),
            out value);
    }

    internal static bool TryParseDateOnly(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out DateOnly value)
    {
        if (format is null)
        {
            return TryParseChars(
                utf8,
                culture,
                static (ReadOnlySpan<char> chars, CultureInfo provider, out DateOnly parsed) =>
                    DateOnly.TryParse(chars, provider, DateTimeStyles.None, out parsed),
                out value);
        }

        return TryParseChars(
            utf8,
            culture,
            format,
            static (ReadOnlySpan<char> chars, CultureInfo provider, string exactFormat, out DateOnly parsed) =>
                DateOnly.TryParseExact(chars, exactFormat, provider, DateTimeStyles.None, out parsed),
            out value);
    }

    internal static bool TryParseTimeOnly(ReadOnlySpan<byte> utf8, CultureInfo culture, string? format, out TimeOnly value)
    {
        if (format is null)
        {
            return TryParseChars(
                utf8,
                culture,
                static (ReadOnlySpan<char> chars, CultureInfo provider, out TimeOnly parsed) =>
                    TimeOnly.TryParse(chars, provider, DateTimeStyles.None, out parsed),
                out value);
        }

        return TryParseChars(
            utf8,
            culture,
            format,
            static (ReadOnlySpan<char> chars, CultureInfo provider, string exactFormat, out TimeOnly parsed) =>
                TimeOnly.TryParseExact(chars, exactFormat, provider, DateTimeStyles.None, out parsed),
            out value);
    }

    internal static bool TryParseEnum<TEnum>(ReadOnlySpan<byte> utf8, out TEnum value)
        where TEnum : struct, Enum
        => TryParseChars(
            utf8,
            CultureInfo.InvariantCulture,
            static (ReadOnlySpan<char> chars, CultureInfo _, out TEnum parsed) =>
                Enum.TryParse(chars, ignoreCase: true, out parsed),
            out value);

    private static bool TryParseChars<T>(
        ReadOnlySpan<byte> utf8,
        CultureInfo culture,
        Utf8CharParser<T> parser,
        out T value)
    {
        int charCount = Encoding.UTF8.GetCharCount(utf8);
        if (charCount <= STACKALLOC_CHAR_THRESHOLD)
        {
            Span<char> chars = stackalloc char[STACKALLOC_CHAR_THRESHOLD];
            int charsWritten = Encoding.UTF8.GetChars(utf8, chars);
            return parser(chars[..charsWritten], culture, out value);
        }

        var rented = ArrayPool<char>.Shared.Rent(charCount);
        try
        {
            int charsWritten = Encoding.UTF8.GetChars(utf8, rented);
            return parser(rented.AsSpan(0, charsWritten), culture, out value);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static bool TryParseChars<T>(
        ReadOnlySpan<byte> utf8,
        CultureInfo culture,
        string format,
        Utf8FormattedCharParser<T> parser,
        out T value)
    {
        int charCount = Encoding.UTF8.GetCharCount(utf8);
        if (charCount <= STACKALLOC_CHAR_THRESHOLD)
        {
            Span<char> chars = stackalloc char[STACKALLOC_CHAR_THRESHOLD];
            int charsWritten = Encoding.UTF8.GetChars(utf8, chars);
            return parser(chars[..charsWritten], culture, format, out value);
        }

        var rented = ArrayPool<char>.Shared.Rent(charCount);
        try
        {
            int charsWritten = Encoding.UTF8.GetChars(utf8, rented);
            return parser(rented.AsSpan(0, charsWritten), culture, format, out value);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static bool IsInvariantCulture(CultureInfo culture)
        => Equals(culture, CultureInfo.InvariantCulture);

    private delegate bool Utf8CharParser<T>(ReadOnlySpan<char> chars, CultureInfo culture, out T value);

    private delegate bool Utf8FormattedCharParser<T>(ReadOnlySpan<char> chars, CultureInfo culture, string format, out T value);
}

#pragma warning restore IDE0350
#pragma warning restore CS1591
