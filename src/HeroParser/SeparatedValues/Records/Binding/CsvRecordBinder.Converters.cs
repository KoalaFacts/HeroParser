using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace HeroParser;

internal sealed partial class CsvRecordBinder<T> where T : class, new()
{
    private static class SetterFactory
    {
        public static Action<T, object?> CreateSetter(PropertyInfo property)
        {
            var target = Expression.Parameter(typeof(T), "target");
            var value = Expression.Parameter(typeof(object), "value");

            var assign = Expression.Assign(
                Expression.Property(target, property),
                Expression.Convert(value, property.PropertyType));

            var lambda = Expression.Lambda<Action<T, object?>>(assign, target, value);
            return lambda.Compile();
        }
    }

    private static class ConverterFactory
    {
        [RequiresUnreferencedCode("Dynamic converter creation uses reflection.")]
        [RequiresDynamicCode("Dynamic converter creation uses MakeGenericType/MakeGenericMethod.")]
        public static ColumnConverter CreateConverter(
            Type type,
            CultureInfo culture,
            string? format,
            IReadOnlyDictionary<Type, InternalCustomConverter>? customConverters)
        {
            var nullableUnderlying = Nullable.GetUnderlyingType(type);
            bool isNullable = type.IsClass || nullableUnderlying is not null;
            var targetType = nullableUnderlying ?? type;

            // Check for custom converter first
            if (customConverters is not null && customConverters.TryGetValue(targetType, out var customConverter))
            {
                return BuildCustomConverter(customConverter, culture, format, isNullable);
            }

            if (targetType == typeof(string))
                return BuildStringConverter(isNullable);

            if (targetType == typeof(int))
                return BuildInt32Converter(isNullable, culture);
            if (targetType == typeof(long))
                return BuildInt64Converter(isNullable, culture);
            if (targetType == typeof(short))
                return BuildInt16Converter(isNullable, culture);
            if (targetType == typeof(byte))
                return BuildByteConverter(isNullable, culture);
            if (targetType == typeof(uint))
                return BuildUInt32Converter(isNullable, culture);
            if (targetType == typeof(ulong))
                return BuildUInt64Converter(isNullable, culture);
            if (targetType == typeof(ushort))
                return BuildUInt16Converter(isNullable, culture);
            if (targetType == typeof(sbyte))
                return BuildSByteConverter(isNullable, culture);
            if (targetType == typeof(double))
                return BuildDoubleConverter(isNullable, culture);
            if (targetType == typeof(float))
                return BuildSingleConverter(isNullable, culture);
            if (targetType == typeof(decimal))
                return BuildDecimalConverter(isNullable, culture);
            if (targetType == typeof(bool))
                return BuildBooleanConverter(isNullable);
            if (targetType == typeof(DateTime))
                return BuildDateTimeConverter(isNullable, culture, format);
            if (targetType == typeof(DateTimeOffset))
                return BuildDateTimeOffsetConverter(isNullable, culture, format);
            if (targetType == typeof(DateOnly))
                return BuildDateOnlyConverter(isNullable, culture, format);
            if (targetType == typeof(TimeOnly))
                return BuildTimeOnlyConverter(isNullable, culture, format);
            if (targetType == typeof(Guid))
                return BuildGuidConverter(isNullable);
            if (targetType == typeof(TimeZoneInfo))
                return BuildTimeZoneInfoConverter(isNullable);

            if (targetType.IsEnum)
                return BuildEnumConverter(targetType, isNullable);

            if (targetType.IsArray)
                return BuildAlwaysNullConverter(isNullable);

            var spanParsableInterface = typeof(ISpanParsable<>).MakeGenericType(targetType);
            try
            {
                if (spanParsableInterface.IsAssignableFrom(targetType))
                {
                    var method = typeof(ConverterFactory)
                        .GetMethod(nameof(BuildSpanParsableConverter), BindingFlags.NonPublic | BindingFlags.Static)!
                        .MakeGenericMethod(targetType);
                    return (ColumnConverter)method.Invoke(null, [isNullable, culture])!;
                }
            }
            catch
            {
                // Unsupported ISpanParsable<T> instantiation, fall through to default converter.
            }

            return BuildAlwaysNullConverter(isNullable);
        }

        private static ColumnConverter BuildCustomConverter(InternalCustomConverter converter, CultureInfo culture, string? format, bool isNullable)
        {
            return (column, out value) =>
            {
                if (isNullable && column.IsEmpty)
                {
                    value = null;
                    return true;
                }

                if (converter(column.CharSpan, culture, format, out value))
                {
                    return true;
                }

                value = null;
                return isNullable;
            };
        }

        private static ColumnConverter BuildStringConverter(bool isNullable)
            => (column, out value) =>
            {
                if (isNullable && column.IsEmpty)
                {
                    value = null;
                    return true;
                }

                value = column.ToString();
                return true;
            };

        private static ColumnConverter BuildInt32Converter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out int result) =>
                int.TryParse(chars, NumberStyles.Integer, culture, out result));

        private static ColumnConverter BuildInt64Converter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out long result) =>
                long.TryParse(chars, NumberStyles.Integer, culture, out result));

        private static ColumnConverter BuildInt16Converter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out short result) =>
                short.TryParse(chars, NumberStyles.Integer, culture, out result));

        private static ColumnConverter BuildByteConverter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out byte result) =>
                byte.TryParse(chars, NumberStyles.Integer, culture, out result));

        private static ColumnConverter BuildUInt32Converter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out uint result) =>
                uint.TryParse(chars, NumberStyles.Integer, culture, out result));

        private static ColumnConverter BuildUInt64Converter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out ulong result) =>
                ulong.TryParse(chars, NumberStyles.Integer, culture, out result));

        private static ColumnConverter BuildUInt16Converter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out ushort result) =>
                ushort.TryParse(chars, NumberStyles.Integer, culture, out result));

        private static ColumnConverter BuildSByteConverter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out sbyte result) =>
                sbyte.TryParse(chars, NumberStyles.Integer, culture, out result));

        private static ColumnConverter BuildDoubleConverter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out double result) =>
                double.TryParse(chars, NumberStyles.Float | NumberStyles.AllowThousands, culture, out result));

        private static ColumnConverter BuildSingleConverter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out float result) =>
                float.TryParse(chars, NumberStyles.Float | NumberStyles.AllowThousands, culture, out result));

        private static ColumnConverter BuildDecimalConverter(bool isNullable, CultureInfo culture)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out decimal result) =>
                decimal.TryParse(chars, NumberStyles.Number, culture, out result));

        private static ColumnConverter BuildBooleanConverter(bool isNullable)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out bool result) =>
                bool.TryParse(chars, out result));

        private static ColumnConverter BuildDateTimeConverter(bool isNullable, CultureInfo culture, string? format)
        {
            if (!string.IsNullOrEmpty(format))
            {
                return WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out DateTime result) =>
                    DateTime.TryParseExact(chars, format, culture, DateTimeStyles.None, out result));
            }

            return WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out DateTime result) =>
                DateTime.TryParse(chars, culture, DateTimeStyles.None, out result));
        }

        private static ColumnConverter BuildDateTimeOffsetConverter(bool isNullable, CultureInfo culture, string? format)
        {
            if (!string.IsNullOrEmpty(format))
            {
                return WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out DateTimeOffset result) =>
                    DateTimeOffset.TryParseExact(chars, format, culture, DateTimeStyles.None, out result));
            }

            return WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out DateTimeOffset result) =>
                DateTimeOffset.TryParse(chars, culture, DateTimeStyles.None, out result));
        }

        private static ColumnConverter BuildDateOnlyConverter(bool isNullable, CultureInfo culture, string? format)
        {
            if (!string.IsNullOrEmpty(format))
            {
                return WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out DateOnly result) =>
                    DateOnly.TryParseExact(chars, format, culture, DateTimeStyles.None, out result));
            }

            return WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out DateOnly result) =>
                DateOnly.TryParse(chars, culture, DateTimeStyles.None, out result));
        }

        private static ColumnConverter BuildTimeOnlyConverter(bool isNullable, CultureInfo culture, string? format)
        {
            if (!string.IsNullOrEmpty(format))
            {
                return WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out TimeOnly result) =>
                    TimeOnly.TryParseExact(chars, format, culture, DateTimeStyles.None, out result));
            }

            return WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out TimeOnly result) =>
                TimeOnly.TryParse(chars, culture, DateTimeStyles.None, out result));
        }

        private static ColumnConverter BuildGuidConverter(bool isNullable)
            => WrapPrimitive(isNullable, (ReadOnlySpan<char> chars, out Guid result) =>
                Guid.TryParse(chars, out result));

        private static ColumnConverter BuildTimeZoneInfoConverter(bool isNullable)
            => WrapPrimitiveColumn(isNullable, (CsvCharSpanColumn column, out TimeZoneInfo result) => column.TryParseTimeZoneInfo(out result));

        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Called from annotated CreateConverter method.")]
        [UnconditionalSuppressMessage("AOT", "IL2070", Justification = "Called from annotated CreateConverter method.")]
        private static ColumnConverter BuildEnumConverter(Type enumType, bool isNullable)
        {
            var method = typeof(ConverterFactory)
                .GetMethod(nameof(EnumConverter), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(enumType);

            return (ColumnConverter)method.Invoke(null, [isNullable])!;
        }

        private static ColumnConverter EnumConverter<TEnum>(bool isNullable) where TEnum : struct, Enum
            => WrapPrimitiveColumn(isNullable, (CsvCharSpanColumn column, out TEnum result) => column.TryParseEnum(out result));

        private static ColumnConverter BuildSpanParsableConverter<TTarget>(bool isNullable, CultureInfo culture)
            where TTarget : notnull, ISpanParsable<TTarget>
            => (column, out value) =>
            {
                if (isNullable && column.IsEmpty)
                {
                    value = null;
                    return true;
                }

                if (TTarget.TryParse(column.CharSpan, culture, out var parsed))
                {
                    value = parsed;
                    return true;
                }

                value = null;
                return false;
            };

        private static ColumnConverter WrapPrimitive<TValue>(
            bool isNullable,
            SpanTryParseDelegate<TValue> tryParse)
        {
            return (column, out value) =>
            {
                if (isNullable && column.IsEmpty)
                {
                    value = null;
                    return true;
                }

                if (tryParse(column.CharSpan, out var parsed))
                {
                    value = parsed!;
                    return true;
                }

                value = null;
                return false;
            };
        }

        private static ColumnConverter WrapPrimitiveColumn<TValue>(
            bool isNullable,
            ColumnTryParseDelegate<TValue> tryParse)
        {
            return (column, out value) =>
            {
                if (isNullable && column.IsEmpty)
                {
                    value = null;
                    return true;
                }

                if (tryParse(column, out var parsed))
                {
                    value = parsed!;
                    return true;
                }

                value = null;
                return false;
            };
        }

        private static ColumnConverter BuildAlwaysNullConverter(bool isNullable)
        {
            if (isNullable)
            {
                return (_, out value) =>
                {
                    value = null;
                    return true;
                };
            }

            return (_, out value) =>
            {
                value = null;
                return false;
            };
        }

        private delegate bool SpanTryParseDelegate<TValue>(ReadOnlySpan<char> chars, out TValue result);
        private delegate bool ColumnTryParseDelegate<TValue>(CsvCharSpanColumn column, out TValue result);
    }
}
