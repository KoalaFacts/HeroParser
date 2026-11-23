using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using HeroParser.Internal;

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
internal readonly struct SpanParserFactory
{
    /// <summary>
    /// Gets a <see cref="SpanParser{TProperty}"/> for the specified type, including nullable support.
    /// </summary>
    [RequiresUnreferencedCode("SpanParserFactory uses reflection for enum parsing.")]
    [RequiresDynamicCode("SpanParserFactory uses MakeGenericMethod for enum parsing.")]
    public SpanParser<TProperty> GetParser<TProperty>()
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
            _ when targetType == typeof(uint) => CreateValueParser(isNullable, static (span, culture) => uint.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(ulong) => CreateValueParser(isNullable, static (span, culture) => ulong.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(ushort) => CreateValueParser(isNullable, static (span, culture) => ushort.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(sbyte) => CreateValueParser(isNullable, static (span, culture) => sbyte.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(decimal) => CreateValueParser(isNullable, static (span, culture) => decimal.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(double) => CreateValueParser(isNullable, static (span, culture) => double.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(float) => CreateValueParser(isNullable, static (span, culture) => float.Parse(span, NumberStyles.Any, culture)),
            _ when targetType == typeof(Half) => CreateValueParser(isNullable, static (span, culture) => Half.Parse(span, NumberStyles.Float | NumberStyles.AllowThousands, culture)),
            _ when targetType == typeof(Int128) => CreateValueParser(isNullable, static (span, culture) => Int128.Parse(span, NumberStyles.Integer, culture)),
            _ when targetType == typeof(UInt128) => CreateValueParser(isNullable, static (span, culture) => UInt128.Parse(span, NumberStyles.Integer, culture)),
            _ when targetType == typeof(BigInteger) => CreateValueParser(isNullable, static (span, culture) => BigInteger.Parse(span, NumberStyles.Integer, culture)),
            _ when targetType == typeof(char) => CreateValueParser(isNullable, static (span, _) => span.Length == 1 ? span[0] : throw new FormatException("Input string must be exactly one character.")),
            _ when targetType == typeof(bool) => CreateValueParser(isNullable, static (span, _) => bool.Parse(span)),
            _ when targetType == typeof(DateTime) => CreateValueParser(isNullable, static (span, culture) => DateTime.Parse(span, culture, DateTimeStyles.None)),
            _ when targetType == typeof(DateTimeOffset) => CreateValueParser(isNullable, static (span, culture) => DateTimeOffset.Parse(span, culture, DateTimeStyles.None)),
            _ when targetType == typeof(DateOnly) => CreateValueParser(isNullable, static (span, culture) => DateOnly.Parse(span, culture)),
            _ when targetType == typeof(TimeOnly) => CreateValueParser(isNullable, static (span, culture) => TimeOnly.Parse(span, culture)),
            _ when targetType == typeof(TimeSpan) => CreateValueParser(isNullable, static (span, culture) => TimeSpan.Parse(span, culture)),
            _ when targetType == typeof(Guid) => CreateValueParser(isNullable, static (span, _) => Guid.Parse(span)),
            _ when targetType == typeof(Uri) => (SpanParser<Uri>)ParseUri,
            _ when targetType == typeof(byte[]) => (SpanParser<byte[]>)ParseBase64Bytes,
            _ when targetType.IsEnum => CreateEnumParser(targetType, isNullable),
            _ when ImplementsSelfInterface(targetType, typeof(ISpanParsable<>)) => CreateSpanParsableParser(targetType, isNullable),
            _ when ImplementsSelfInterface(targetType, typeof(IUtf8SpanParsable<>)) => CreateUtf8SpanParsableParser(targetType, isNullable),
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

    [RequiresUnreferencedCode("Uses MakeGenericMethod for enum parsing.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for enum parsing.")]
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

    private static Uri ParseUri(ReadOnlySpan<char> span, CultureInfo _)
    {
        if (span.IsEmpty || span.IsWhiteSpace())
            return null!;
        if (Uri.TryCreate(new string(span.Trim()), UriKind.RelativeOrAbsolute, out var uri))
            return uri;
        throw new FormatException("Input string was not in a valid URI format.");
    }

    private static byte[] ParseBase64Bytes(ReadOnlySpan<char> span, CultureInfo _)
    {
        if (Utf8SpanParser.TryParseBase64Chars(span, out var bytes))
            return bytes;
        throw new FormatException("Input string was not valid Base64.");
    }

    [RequiresUnreferencedCode("Uses GetInterfaces and MakeGenericMethod.")]
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

    [RequiresUnreferencedCode("Uses MakeGenericMethod for ISpanParsable<T> parsing.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for ISpanParsable<T> parsing.")]
    private static object CreateSpanParsableParser(Type targetType, bool isNullable)
    {
        if (targetType.IsValueType)
        {
            var method = typeof(SpanParserFactory).GetMethod(
                nameof(CreateValueSpanParsableGeneric),
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return method.MakeGenericMethod(targetType).Invoke(null, [isNullable])!;
        }

        var refMethod = typeof(SpanParserFactory).GetMethod(
            nameof(CreateRefSpanParsableGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return refMethod.MakeGenericMethod(targetType).Invoke(null, null)!;
    }

    private static object CreateValueSpanParsableGeneric<TValue>(bool isNullable) where TValue : struct, ISpanParsable<TValue>
        => CreateValueParser<TValue>(isNullable, static (span, culture) => TValue.Parse(span, culture));

    private static SpanParser<TValue> CreateRefSpanParsableGeneric<TValue>() where TValue : class, ISpanParsable<TValue>
        => static (span, culture) =>
        {
            if (span.IsEmpty || span.IsWhiteSpace())
                return null!;
            return TValue.Parse(span, culture);
        };

    [RequiresUnreferencedCode("Uses MakeGenericMethod for IUtf8SpanParsable<T> parsing.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for IUtf8SpanParsable<T> parsing.")]
    private static object CreateUtf8SpanParsableParser(Type targetType, bool isNullable)
    {
        if (targetType.IsValueType)
        {
            var method = typeof(SpanParserFactory).GetMethod(
                nameof(CreateValueUtf8SpanParsableGeneric),
                BindingFlags.NonPublic | BindingFlags.Static)!;
            return method.MakeGenericMethod(targetType).Invoke(null, [isNullable])!;
        }

        var refMethod = typeof(SpanParserFactory).GetMethod(
            nameof(CreateRefUtf8SpanParsableGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return refMethod.MakeGenericMethod(targetType).Invoke(null, null)!;
    }

    private static object CreateValueUtf8SpanParsableGeneric<TValue>(bool isNullable) where TValue : struct, IUtf8SpanParsable<TValue>
        => CreateValueParser<TValue>(isNullable, static (span, culture) =>
            Utf8SpanParser.TryParseUtf8FromChars<TValue>(span, culture, out var parsed)
                ? parsed
                : throw new FormatException($"Failed to parse '{typeof(TValue).Name}'."));

    private static SpanParser<TValue> CreateRefUtf8SpanParsableGeneric<TValue>() where TValue : class, IUtf8SpanParsable<TValue>
        => static (span, culture) =>
        {
            if (span.IsEmpty || span.IsWhiteSpace())
                return null!;
            return Utf8SpanParser.TryParseUtf8FromChars<TValue>(span, culture, out var parsed) && parsed is not null
                ? parsed
                : throw new FormatException($"Failed to parse '{typeof(TValue).Name}'.");
        };
}
