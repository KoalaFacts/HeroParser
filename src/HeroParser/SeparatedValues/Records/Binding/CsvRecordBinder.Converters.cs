using HeroParser.SeparatedValues;
using System;
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
        public static ColumnConverter CreateConverter(Type type)
        {
            var nullableUnderlying = Nullable.GetUnderlyingType(type);
            bool isNullable = type.IsClass || nullableUnderlying is not null;
            var targetType = nullableUnderlying ?? type;

            if (targetType == typeof(string))
                return BuildStringConverter(isNullable);

            if (targetType == typeof(int))
                return BuildInt32Converter(isNullable);
            if (targetType == typeof(long))
                return BuildInt64Converter(isNullable);
            if (targetType == typeof(short))
                return BuildInt16Converter(isNullable);
            if (targetType == typeof(byte))
                return BuildByteConverter(isNullable);
            if (targetType == typeof(uint))
                return BuildUInt32Converter(isNullable);
            if (targetType == typeof(ulong))
                return BuildUInt64Converter(isNullable);
            if (targetType == typeof(ushort))
                return BuildUInt16Converter(isNullable);
            if (targetType == typeof(sbyte))
                return BuildSByteConverter(isNullable);
            if (targetType == typeof(double))
                return BuildDoubleConverter(isNullable);
            if (targetType == typeof(float))
                return BuildSingleConverter(isNullable);
            if (targetType == typeof(decimal))
                return BuildDecimalConverter(isNullable);
            if (targetType == typeof(bool))
                return BuildBooleanConverter(isNullable);
            if (targetType == typeof(DateTime))
                return BuildDateTimeConverter(isNullable);
            if (targetType == typeof(DateTimeOffset))
                return BuildDateTimeOffsetConverter(isNullable);
            if (targetType == typeof(DateOnly))
                return BuildDateOnlyConverter(isNullable);
            if (targetType == typeof(TimeOnly))
                return BuildTimeOnlyConverter(isNullable);
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
                    return (ColumnConverter)method.Invoke(null, [isNullable])!;
                }
            }
            catch
            {
                // Unsupported ISpanParsable<T> instantiation, fall through to default converter.
            }

            return BuildAlwaysNullConverter(isNullable);
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

        private static ColumnConverter BuildInt32Converter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out int result) => column.TryParseInt32(out result));

        private static ColumnConverter BuildInt64Converter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out long result) => column.TryParseInt64(out result));

        private static ColumnConverter BuildInt16Converter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out short result) => column.TryParseInt16(out result));

        private static ColumnConverter BuildByteConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out byte result) => column.TryParseByte(out result));

        private static ColumnConverter BuildUInt32Converter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out uint result) => column.TryParseUInt32(out result));

        private static ColumnConverter BuildUInt64Converter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out ulong result) => column.TryParseUInt64(out result));

        private static ColumnConverter BuildUInt16Converter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out ushort result) => column.TryParseUInt16(out result));

        private static ColumnConverter BuildSByteConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out sbyte result) => column.TryParseSByte(out result));

        private static ColumnConverter BuildDoubleConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out double result) => column.TryParseDouble(out result));

        private static ColumnConverter BuildSingleConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out float result) => column.TryParseSingle(out result));

        private static ColumnConverter BuildDecimalConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out decimal result) => column.TryParseDecimal(out result));

        private static ColumnConverter BuildBooleanConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out bool result) => column.TryParseBoolean(out result));

        private static ColumnConverter BuildDateTimeConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out DateTime result) => column.TryParseDateTime(out result));

        private static ColumnConverter BuildDateTimeOffsetConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out DateTimeOffset result) => column.TryParseDateTimeOffset(out result));

        private static ColumnConverter BuildDateOnlyConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out DateOnly result) => column.TryParseDateOnly(out result));

        private static ColumnConverter BuildTimeOnlyConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out TimeOnly result) => column.TryParseTimeOnly(out result));

        private static ColumnConverter BuildGuidConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out Guid result) => column.TryParseGuid(out result));

        private static ColumnConverter BuildTimeZoneInfoConverter(bool isNullable)
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out TimeZoneInfo result) => column.TryParseTimeZoneInfo(out result));

        private static ColumnConverter BuildEnumConverter(Type enumType, bool isNullable)
        {
            var method = typeof(ConverterFactory)
                .GetMethod(nameof(EnumConverter), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(enumType);

            return (ColumnConverter)method.Invoke(null, [isNullable])!;
        }

        private static ColumnConverter EnumConverter<TEnum>(bool isNullable) where TEnum : struct, Enum
            => WrapPrimitive(isNullable, (CsvCharSpanColumn column, out TEnum result) => column.TryParseEnum(out result));

        private static ColumnConverter BuildSpanParsableConverter<TTarget>(bool isNullable)
            where TTarget : notnull, ISpanParsable<TTarget>
            => (column, out value) =>
            {
                if (isNullable && column.IsEmpty)
                {
                    value = null;
                    return true;
                }

                if (TTarget.TryParse(column.CharSpan, CultureInfo.InvariantCulture, out var parsed))
                {
                    value = parsed;
                    return true;
                }

                value = null;
                return false;
            };

        private static ColumnConverter WrapPrimitive<TValue>(
            bool isNullable,
            TryParseDelegate<TValue> tryParse)
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

        private delegate bool TryParseDelegate<TValue>(CsvCharSpanColumn column, out TValue result);
    }
}
