using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using HeroParser.Internal;

namespace HeroParser.FixedWidths.Mapping;

/// <summary>
/// Delegate for parsing a <see cref="ReadOnlySpan{T}"/> of UTF-8 bytes into a typed value.
/// </summary>
/// <typeparam name="TProperty">The target type.</typeparam>
/// <param name="span">The UTF-8 byte span to parse.</param>
/// <param name="culture">The culture for parsing.</param>
/// <returns>The parsed value.</returns>
internal delegate TProperty Utf8SpanParser<out TProperty>(ReadOnlySpan<byte> span, CultureInfo culture);

/// <summary>
/// Factory for creating <see cref="Utf8SpanParser{TProperty}"/> instances for common CLR types.
/// Mirrors <see cref="SeparatedValues.Mapping.SpanParserFactory"/> semantics for map-based binding.
/// </summary>
internal readonly struct Utf8SpanParserFactory
{
    [RequiresUnreferencedCode("Utf8SpanParserFactory uses reflection for enum parsing.")]
    [RequiresDynamicCode("Utf8SpanParserFactory uses MakeGenericMethod for enum parsing.")]
    internal Utf8SpanParser<TProperty> GetParser<TProperty>()
    {
        var type = typeof(TProperty);
        var underlyingType = Nullable.GetUnderlyingType(type);
        var isNullable = underlyingType is not null;
        var targetType = underlyingType ?? type;

        object parser = targetType switch
        {
            _ when targetType == typeof(string) => (Utf8SpanParser<string>)((span, _) => new Utf8SpanParser(span).Decode()),
            _ when targetType == typeof(int) => CreateValueParser(isNullable, static (span, culture) => ParseInt32(span, culture)),
            _ when targetType == typeof(long) => CreateValueParser(isNullable, static (span, culture) => ParseInt64(span, culture)),
            _ when targetType == typeof(short) => CreateValueParser(isNullable, static (span, culture) => ParseInt16(span, culture)),
            _ when targetType == typeof(byte) => CreateValueParser(isNullable, static (span, culture) => ParseByte(span, culture)),
            _ when targetType == typeof(uint) => CreateValueParser(isNullable, static (span, culture) => ParseUInt32(span, culture)),
            _ when targetType == typeof(ulong) => CreateValueParser(isNullable, static (span, culture) => ParseUInt64(span, culture)),
            _ when targetType == typeof(ushort) => CreateValueParser(isNullable, static (span, culture) => ParseUInt16(span, culture)),
            _ when targetType == typeof(sbyte) => CreateValueParser(isNullable, static (span, culture) => ParseSByte(span, culture)),
            _ when targetType == typeof(decimal) => CreateValueParser(isNullable, static (span, culture) => ParseDecimal(span, culture)),
            _ when targetType == typeof(double) => CreateValueParser(isNullable, static (span, culture) => ParseDouble(span, culture)),
            _ when targetType == typeof(float) => CreateValueParser(isNullable, static (span, culture) => ParseSingle(span, culture)),
            _ when targetType == typeof(Half) => CreateValueParser(isNullable, static (span, culture) => ParseHalf(span, culture)),
            _ when targetType == typeof(Int128) => CreateValueParser(isNullable, static (span, culture) => ParseInt128(span, culture)),
            _ when targetType == typeof(UInt128) => CreateValueParser(isNullable, static (span, culture) => ParseUInt128(span, culture)),
            _ when targetType == typeof(BigInteger) => CreateValueParser(isNullable, static (span, culture) => ParseBigInteger(span, culture)),
            _ when targetType == typeof(char) => CreateValueParser(isNullable, static (span, _) => ParseChar(span)),
            _ when targetType == typeof(bool) => CreateValueParser(isNullable, static (span, _) => ParseBoolean(span)),
            _ when targetType == typeof(DateTime) => CreateValueParser(isNullable, static (span, culture) => ParseDateTime(span, culture)),
            _ when targetType == typeof(DateTimeOffset) => CreateValueParser(isNullable, static (span, culture) => ParseDateTimeOffset(span, culture)),
            _ when targetType == typeof(DateOnly) => CreateValueParser(isNullable, static (span, culture) => ParseDateOnly(span, culture)),
            _ when targetType == typeof(TimeOnly) => CreateValueParser(isNullable, static (span, culture) => ParseTimeOnly(span, culture)),
            _ when targetType == typeof(TimeSpan) => CreateValueParser(isNullable, static (span, culture) => ParseTimeSpan(span, culture)),
            _ when targetType == typeof(Guid) => CreateValueParser(isNullable, static (span, _) => ParseGuid(span)),
            _ when targetType == typeof(Uri) => (Utf8SpanParser<Uri>)ParseUri,
            _ when targetType == typeof(byte[]) => (Utf8SpanParser<byte[]>)ParseBase64Bytes,
            _ when targetType.IsEnum => CreateEnumParser(targetType, isNullable),
            _ when ImplementsSelfInterface(targetType, typeof(IUtf8SpanParsable<>)) => CreateUtf8SpanParsableParser(targetType, isNullable),
            _ when ImplementsSelfInterface(targetType, typeof(ISpanParsable<>)) => CreateSpanParsableParser(targetType, isNullable),
            _ => throw new NotSupportedException($"Type '{type.FullName}' is not supported for fixed-width mapping.")
        };

        return (Utf8SpanParser<TProperty>)parser;
    }

    private static object CreateValueParser<TValue>(bool isNullable, Utf8SpanParser<TValue> parser) where TValue : struct
    {
        if (!isNullable)
            return parser;

        Utf8SpanParser<TValue?> nullableParser = (span, culture) =>
        {
            if (span.IsEmpty || new Utf8SpanParser(span).IsNullOrWhiteSpace())
                return null;

            return parser(span, culture);
        };

        return nullableParser;
    }

    [RequiresUnreferencedCode("Uses MakeGenericMethod for enum parsing.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for enum parsing.")]
    private static object CreateEnumParser(Type enumType, bool isNullable)
    {
        var method = typeof(Utf8SpanParserFactory).GetMethod(
            nameof(CreateEnumParserGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return isNullable
            ? method.MakeGenericMethod(typeof(Nullable<>).MakeGenericType(enumType)).Invoke(null, [isNullable])!
            : method.MakeGenericMethod(enumType).Invoke(null, [isNullable])!;
    }

    private static Utf8SpanParser<TEnum> CreateEnumParserGeneric<TEnum>(bool isNullable)
    {
        var underlyingType = Nullable.GetUnderlyingType(typeof(TEnum)) ?? typeof(TEnum);
        return (span, _) =>
        {
            if (isNullable && (span.IsEmpty || new Utf8SpanParser(span).IsNullOrWhiteSpace()))
                return default!;

            return (TEnum)Enum.Parse(underlyingType, new Utf8SpanParser(span).Decode());
        };
    }

    private static int ParseInt32(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseInt32(culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static long ParseInt64(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseInt64(culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static short ParseInt16(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseInt16(culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static byte ParseByte(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseByte(culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static decimal ParseDecimal(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseDecimal(culture, NumberStyles.Number, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static double ParseDouble(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseDouble(culture, NumberStyles.Float | NumberStyles.AllowThousands, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static float ParseSingle(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseSingle(culture, NumberStyles.Float | NumberStyles.AllowThousands, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static DateTime ParseDateTime(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseDateTime(culture, DateTimeStyles.None, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static DateOnly ParseDateOnly(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseDateOnly(culture, DateTimeStyles.None, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static TimeOnly ParseTimeOnly(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseTimeOnly(culture, DateTimeStyles.None, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static Guid ParseGuid(ReadOnlySpan<byte> span)
    {
        if (new Utf8SpanParser(span).TryParseGuid(out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static uint ParseUInt32(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseUInt32(culture, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static ulong ParseUInt64(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseUInt64(culture, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static ushort ParseUInt16(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseUInt16(culture, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static sbyte ParseSByte(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseSByte(culture, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static bool ParseBoolean(ReadOnlySpan<byte> span)
    {
        if (new Utf8SpanParser(span).TryParseBoolean(out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static DateTimeOffset ParseDateTimeOffset(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseDateTimeOffset(culture, DateTimeStyles.None, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static TimeSpan ParseTimeSpan(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseTimeSpan(culture, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static char ParseChar(ReadOnlySpan<byte> span)
    {
        if (new Utf8SpanParser(span).TryParseChar(out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static Half ParseHalf(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseHalf(culture, NumberStyles.Float | NumberStyles.AllowThousands, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static Int128 ParseInt128(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseInt128(culture, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static UInt128 ParseUInt128(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseUInt128(culture, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static BigInteger ParseBigInteger(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (new Utf8SpanParser(span).TryParseBigInteger(culture, out var value))
            return value;
        ThrowFormatException();
        return default;
    }

    private static Uri ParseUri(ReadOnlySpan<byte> span, CultureInfo _)
    {
        var parser = new Utf8SpanParser(span);
        if (span.IsEmpty || parser.IsNullOrWhiteSpace())
            return null!;
        if (parser.TryParseUri(out var uri))
            return uri;
        ThrowFormatException();
        return null!;
    }

    private static byte[] ParseBase64Bytes(ReadOnlySpan<byte> span, CultureInfo _)
    {
        if (new Utf8SpanParser(span).TryParseBase64Bytes(out var bytes))
            return bytes;
        ThrowFormatException();
        return [];
    }

    private static bool ImplementsSelfInterface(Type type, Type openGenericInterface)
    {
        if (type.IsAbstract || type.IsInterface)
            return false;
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType &&
                iface.GetGenericTypeDefinition() == openGenericInterface &&
                iface.GetGenericArguments()[0] == type)
            {
                return true;
            }
        }
        return false;
    }

    [RequiresUnreferencedCode("Uses MakeGenericMethod for IUtf8SpanParsable<T> parsing.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for IUtf8SpanParsable<T> parsing.")]
    private static object CreateUtf8SpanParsableParser(Type targetType, bool isNullable)
    {
        if (targetType.IsValueType)
        {
            var method = typeof(Utf8SpanParserFactory).GetMethod(
                nameof(CreateValueUtf8SpanParsableGeneric),
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return method.MakeGenericMethod(targetType).Invoke(null, [isNullable])!;
        }

        var refMethod = typeof(Utf8SpanParserFactory).GetMethod(
            nameof(CreateRefUtf8SpanParsableGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return refMethod.MakeGenericMethod(targetType).Invoke(null, null)!;
    }

    private static object CreateValueUtf8SpanParsableGeneric<TValue>(bool isNullable) where TValue : struct, IUtf8SpanParsable<TValue>
        => CreateValueParser<TValue>(isNullable, static (span, culture) => TValue.Parse(span, culture));

    private static Utf8SpanParser<TValue> CreateRefUtf8SpanParsableGeneric<TValue>() where TValue : class, IUtf8SpanParsable<TValue>
        => static (span, culture) =>
        {
            if (span.IsEmpty || new Utf8SpanParser(span).IsNullOrWhiteSpace())
                return null!;
            return TValue.Parse(span, culture);
        };

    [RequiresUnreferencedCode("Uses MakeGenericMethod for ISpanParsable<T> parsing.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for ISpanParsable<T> parsing.")]
    private static object CreateSpanParsableParser(Type targetType, bool isNullable)
    {
        if (targetType.IsValueType)
        {
            var method = typeof(Utf8SpanParserFactory).GetMethod(
                nameof(CreateValueSpanParsableGeneric),
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return method.MakeGenericMethod(targetType).Invoke(null, [isNullable])!;
        }

        var refMethod = typeof(Utf8SpanParserFactory).GetMethod(
            nameof(CreateRefSpanParsableGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return refMethod.MakeGenericMethod(targetType).Invoke(null, null)!;
    }

    private static object CreateValueSpanParsableGeneric<TValue>(bool isNullable) where TValue : struct, ISpanParsable<TValue>
        => CreateValueParser<TValue>(isNullable, static (span, culture) => new Utf8SpanParser(span).ParseIsomorphic<TValue>(culture));

    private static Utf8SpanParser<TValue> CreateRefSpanParsableGeneric<TValue>() where TValue : class, ISpanParsable<TValue>
        => static (span, culture) =>
        {
            if (span.IsEmpty || new Utf8SpanParser(span).IsNullOrWhiteSpace())
                return null!;
            return new Utf8SpanParser(span).ParseIsomorphic<TValue>(culture);
        };

    [DoesNotReturn]
    private static void ThrowFormatException() => throw new FormatException("Input string was not in a correct format.");
}
