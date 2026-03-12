using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace HeroParser.SeparatedValues.Mapping;

/// <summary>
/// Delegate for parsing a <see cref="ReadOnlySpan{T}"/> of <see cref="char"/> into a typed value.
/// </summary>
/// <typeparam name="TProperty">The target type.</typeparam>
/// <param name="span">The character span to parse.</param>
/// <param name="culture">The culture for parsing.</param>
/// <returns>The parsed value.</returns>
internal delegate TProperty SpanParser<out TProperty>(ReadOnlySpan<char> span, CultureInfo culture);

/// <summary>
/// Factory for creating <see cref="SpanParser{TProperty}"/> instances for common CLR types.
/// Supports primitives, nullable wrappers, enums, strings, DateTime, DateOnly, TimeOnly, and Guid.
/// </summary>
[RequiresUnreferencedCode("SpanParserFactory uses reflection for enum parsing.")]
[RequiresDynamicCode("SpanParserFactory uses MakeGenericMethod for enum parsing.")]
internal static class SpanParserFactory
{
    /// <summary>
    /// Gets a <see cref="SpanParser{TProperty}"/> for the specified type, including nullable support.
    /// </summary>
    public static SpanParser<TProperty> GetParser<TProperty>()
    {
        var type = typeof(TProperty);
        var underlyingType = Nullable.GetUnderlyingType(type);
        var isNullable = underlyingType is not null;
        var targetType = underlyingType ?? type;

        object parser = targetType switch
        {
            _ when targetType == typeof(string) => (SpanParser<string>)((span, _) => span.ToString()),
            _ when targetType == typeof(int) => CreateValueParser(isNullable, static (span, culture) => int.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(long) => CreateValueParser(isNullable, static (span, culture) => long.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(short) => CreateValueParser(isNullable, static (span, culture) => short.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(byte) => CreateValueParser(isNullable, static (span, culture) => byte.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(decimal) => CreateValueParser(isNullable, static (span, culture) => decimal.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(double) => CreateValueParser(isNullable, static (span, culture) => double.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(float) => CreateValueParser(isNullable, static (span, culture) => float.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(bool) => CreateValueParser(isNullable, static (span, _) => bool.Parse(span)),
            _ when targetType == typeof(DateTime) => CreateValueParser(isNullable, static (span, culture) => DateTime.Parse(span, culture, DateTimeStyles.None)),
            _ when targetType == typeof(DateOnly) => CreateValueParser(isNullable, static (span, culture) => DateOnly.Parse(span, culture)),
            _ when targetType == typeof(TimeOnly) => CreateValueParser(isNullable, static (span, culture) => TimeOnly.Parse(span, culture)),
            _ when targetType == typeof(Guid) => CreateValueParser(isNullable, static (span, _) => Guid.Parse(span)),
            _ when targetType.IsEnum => CreateEnumParser(targetType, isNullable),
            _ => throw new NotSupportedException($"Type '{type.FullName}' is not supported for CSV mapping.")
        };

        return (SpanParser<TProperty>)parser;
    }

    private static object CreateValueParser<TValue>(bool isNullable, SpanParser<TValue> parser) where TValue : struct
    {
        if (!isNullable)
            return parser;

        SpanParser<TValue?> nullableParser = (span, culture) =>
        {
            if (span.IsEmpty || span.IsWhiteSpace())
                return null;
            return parser(span, culture);
        };
        return nullableParser;
    }

    private static object CreateEnumParser(Type enumType, bool isNullable)
    {
        var method = typeof(SpanParserFactory).GetMethod(
            nameof(CreateEnumParserGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return isNullable
            ? method.MakeGenericMethod(typeof(Nullable<>).MakeGenericType(enumType)).Invoke(null, [isNullable])!
            : method.MakeGenericMethod(enumType).Invoke(null, [isNullable])!;
    }

    private static SpanParser<TEnum> CreateEnumParserGeneric<TEnum>(bool isNullable)
    {
        var underlyingType = Nullable.GetUnderlyingType(typeof(TEnum)) ?? typeof(TEnum);
        return (span, _) =>
        {
            if (isNullable && (span.IsEmpty || span.IsWhiteSpace()))
                return default!;
            return (TEnum)Enum.Parse(underlyingType, span);
        };
    }
}
