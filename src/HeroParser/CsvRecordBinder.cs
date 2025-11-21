using HeroParser.SeparatedValues;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace HeroParser;

internal sealed class CsvRecordBinder<T> where T : class, new()
{
    private readonly IReadOnlyList<MemberBinding> bindings;
    private readonly CsvRecordOptions recordOptions;
    private bool resolved;
    private readonly StringComparison headerComparison;

    private CsvRecordBinder(CsvRecordOptions recordOptions, IReadOnlyList<BindingTemplate> templates)
    {
        this.recordOptions = recordOptions;
        headerComparison = recordOptions.CaseSensitiveHeaders ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        bindings = InstantiateBindings(templates);

        if (!recordOptions.HasHeaderRow)
        {
            ResolveWithoutHeader();
        }
    }

    public static CsvRecordBinder<T> Create(CsvRecordOptions? options)
        => CreateFromTemplates(options, CreateTemplatesFromReflection());

    internal static CsvRecordBinder<T> CreateFromTemplates(
        CsvRecordOptions? options,
        IReadOnlyList<BindingTemplate> templates)
    {
        var resolvedOptions = options ?? CsvRecordOptions.Default;
        return new CsvRecordBinder<T>(resolvedOptions, templates);
    }

    public bool NeedsHeaderResolution => recordOptions.HasHeaderRow && !resolved;

    public void BindHeader(CsvCharSpanRow headerRow, int rowNumber)
    {
        if (!recordOptions.HasHeaderRow)
            return;

        foreach (var binding in bindings)
        {
            if (binding.AttributeIndex.HasValue)
            {
                binding.ResolvedIndex = binding.AttributeIndex.Value;
                continue;
            }

            var index = FindHeaderIndex(headerRow, binding.HeaderName);
            if (index >= 0)
            {
                binding.ResolvedIndex = index;
                continue;
            }

            if (!recordOptions.AllowMissingColumns)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Column '{binding.HeaderName}' not found in header.",
                    rowNumber);
            }
        }

        resolved = true;
    }

    public T Bind(CsvCharSpanRow row, int rowNumber)
    {
        EnsureResolved(rowNumber);

        var instance = new T();

        foreach (var binding in bindings)
        {
            if (!binding.ResolvedIndex.HasValue)
            {
                if (recordOptions.AllowMissingColumns)
                {
                    continue;
                }

                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"No column mapping found for member '{binding.MemberName}'.",
                    rowNumber);
            }

            var columnIndex = binding.ResolvedIndex.Value;
            if (columnIndex >= row.ColumnCount)
            {
                if (recordOptions.AllowMissingColumns)
                {
                    continue;
                }

                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row has only {row.ColumnCount} columns but '{binding.MemberName}' expects index {columnIndex}.",
                    rowNumber,
                    columnIndex + 1);
            }

            var column = row[columnIndex];
            if (!binding.TryAssign(instance, column))
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Failed to convert column {columnIndex + 1} to {binding.TargetType.Name} for member '{binding.MemberName}'.",
                    rowNumber,
                    columnIndex + 1);
            }
        }

        return instance;
    }

    private void EnsureResolved(int rowNumber)
    {
        if (resolved)
            return;

        if (recordOptions.HasHeaderRow)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                "Header row has not been processed yet.",
                rowNumber);
        }

        ResolveWithoutHeader();
    }

    private int FindHeaderIndex(CsvCharSpanRow headerRow, string headerName)
    {
        var target = headerName.AsSpan();
        for (int i = 0; i < headerRow.ColumnCount; i++)
        {
            var candidate = headerRow[i].CharSpan;
            if (headerComparison == StringComparison.Ordinal)
            {
                if (candidate.SequenceEqual(target))
                    return i;
            }
            else if (candidate.Equals(target, headerComparison))
            {
                return i;
            }
        }

        return -1;
    }

    private void ResolveWithoutHeader()
    {
        int ordinal = 0;
        foreach (var binding in bindings)
        {
            binding.ResolvedIndex = binding.AttributeIndex ?? ordinal++;
        }

        resolved = true;
    }

    private List<MemberBinding> InstantiateBindings(IReadOnlyList<BindingTemplate> templates)
    {
        var list = new List<MemberBinding>(templates.Count);
        foreach (var template in templates)
        {
            list.Add(new MemberBinding(
                template.MemberName,
                template.TargetType,
                template.HeaderName,
                template.AttributeIndex,
                template.Converter,
                template.Setter));
        }
        return list;
    }

    private static IReadOnlyList<BindingTemplate> CreateTemplatesFromReflection()
        => bindingCache.GetOrAdd(typeof(T), _ => BuildTemplates());

    private static List<BindingTemplate> BuildTemplates()
    {
        var members = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod is { IsStatic: false })
            .OrderBy(p => p.MetadataToken);

        var bindings = new List<BindingTemplate>();
        foreach (var property in members)
        {
            var attribute = property.GetCustomAttribute<CsvColumnAttribute>();
            var headerName = !string.IsNullOrWhiteSpace(attribute?.Name) ? attribute!.Name! : property.Name;
            int? attributeIndex = attribute is { Index: >= 0 } ? attribute.Index : null;
            var converter = ConverterFactory.CreateConverter(property.PropertyType);
            var setter = SetterFactory.CreateSetter(property);

            bindings.Add(new BindingTemplate(
                property.Name,
                property.PropertyType,
                headerName,
                attributeIndex,
                converter,
                setter));
        }

        return bindings;
    }

    private sealed class MemberBinding
    {
        public MemberBinding(
            string memberName,
            Type targetType,
            string headerName,
            int? attributeIndex,
            ColumnConverter converter,
            Action<T, object?> setter)
        {
            MemberName = memberName;
            TargetType = targetType;
            HeaderName = headerName;
            AttributeIndex = attributeIndex;
            Converter = converter;
            Setter = setter;
        }

        public string MemberName { get; }
        public Type TargetType { get; }
        public string HeaderName { get; }
        public int? AttributeIndex { get; }
        public int? ResolvedIndex { get; set; }
        private ColumnConverter Converter { get; }
        private Action<T, object?> Setter { get; }

        public bool TryAssign(T instance, CsvCharSpanColumn column)
        {
            if (!Converter(column, out var value))
                return false;

            Setter(instance, value);
            return true;
        }
    }

    internal sealed record BindingTemplate(
        string MemberName,
        Type TargetType,
        string HeaderName,
        int? AttributeIndex,
        ColumnConverter Converter,
        Action<T, object?> Setter);

    private static readonly ConcurrentDictionary<Type, List<BindingTemplate>> bindingCache = new();

    internal delegate bool ColumnConverter(CsvCharSpanColumn column, out object? value);

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
