using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using HeroParser.FixedWidths.Records.Binding;

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
[RequiresUnreferencedCode("Utf8SpanParserFactory uses reflection for enum parsing.")]
[RequiresDynamicCode("Utf8SpanParserFactory uses MakeGenericMethod for enum parsing.")]
internal static class Utf8SpanParserFactory
{
    public static Utf8SpanParser<TProperty> GetParser<TProperty>()
    {
        var type = typeof(TProperty);
        var underlyingType = Nullable.GetUnderlyingType(type);
        var isNullable = underlyingType is not null;
        var targetType = underlyingType ?? type;

        object parser = targetType switch
        {
            _ when targetType == typeof(string) => (Utf8SpanParser<string>)((span, _) => FixedWidthUtf8BindingHelper.Decode(span)),
            _ when targetType == typeof(int) => CreateValueParser<int>(isNullable, static (span, culture) => ParseInt32(span, culture)),
            _ when targetType == typeof(long) => CreateValueParser<long>(isNullable, static (span, culture) => ParseInt64(span, culture)),
            _ when targetType == typeof(short) => CreateValueParser<short>(isNullable, static (span, culture) => ParseInt16(span, culture)),
            _ when targetType == typeof(byte) => CreateValueParser<byte>(isNullable, static (span, culture) => ParseByte(span, culture)),
            _ when targetType == typeof(decimal) => CreateValueParser<decimal>(isNullable, static (span, culture) => ParseDecimal(span, culture)),
            _ when targetType == typeof(double) => CreateValueParser<double>(isNullable, static (span, culture) => ParseDouble(span, culture)),
            _ when targetType == typeof(float) => CreateValueParser<float>(isNullable, static (span, culture) => ParseSingle(span, culture)),
            _ when targetType == typeof(bool) => CreateValueParser<bool>(isNullable, static (span, _) => bool.Parse(FixedWidthUtf8BindingHelper.Decode(span))),
            _ when targetType == typeof(DateTime) => CreateValueParser<DateTime>(isNullable, static (span, culture) => ParseDateTime(span, culture)),
            _ when targetType == typeof(DateOnly) => CreateValueParser<DateOnly>(isNullable, static (span, culture) => ParseDateOnly(span, culture)),
            _ when targetType == typeof(TimeOnly) => CreateValueParser<TimeOnly>(isNullable, static (span, culture) => ParseTimeOnly(span, culture)),
            _ when targetType == typeof(Guid) => CreateValueParser<Guid>(isNullable, static (span, _) => ParseGuid(span)),
            _ when targetType.IsEnum => CreateEnumParser(targetType, isNullable),
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
            if (span.IsEmpty || FixedWidthUtf8BindingHelper.IsNullOrWhiteSpace(span))
                return null;

            return parser(span, culture);
        };

        return nullableParser;
    }

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
            if (isNullable && (span.IsEmpty || FixedWidthUtf8BindingHelper.IsNullOrWhiteSpace(span)))
                return default!;

            return (TEnum)Enum.Parse(underlyingType, FixedWidthUtf8BindingHelper.Decode(span));
        };
    }

    private static int ParseInt32(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseInt32(span, culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static long ParseInt64(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseInt64(span, culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static short ParseInt16(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseInt16(span, culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static byte ParseByte(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseByte(span, culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static decimal ParseDecimal(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseDecimal(span, culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static double ParseDouble(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseDouble(span, culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static float ParseSingle(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseSingle(span, culture, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static DateTime ParseDateTime(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseDateTime(span, culture, format: null, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static DateOnly ParseDateOnly(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseDateOnly(span, culture, format: null, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static TimeOnly ParseTimeOnly(ReadOnlySpan<byte> span, CultureInfo culture)
    {
        if (FixedWidthUtf8BindingHelper.TryParseTimeOnly(span, culture, format: null, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    private static Guid ParseGuid(ReadOnlySpan<byte> span)
    {
        if (FixedWidthUtf8BindingHelper.TryParseGuid(span, out var value))
            return value;

        ThrowFormatException();
        return default;
    }

    [DoesNotReturn]
    private static void ThrowFormatException() => throw new FormatException("Input string was not in a correct format.");
}
