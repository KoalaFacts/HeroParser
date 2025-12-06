using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using HeroParser.FixedWidths.Records;
using CustomConverterDictionary = System.Collections.Generic.IReadOnlyDictionary<System.Type, HeroParser.FixedWidths.Records.InternalFixedWidthConverter>;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Binds fixed-width row data to typed record instances.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
internal sealed class FixedWidthRecordBinder<T> : IFixedWidthBinder<T> where T : class, new()
{
    private static readonly ConcurrentDictionary<Type, List<BindingTemplate>> bindingCache = new();

    private readonly IReadOnlyList<FieldBinding> bindings;
    private readonly CultureInfo culture;
    private readonly FixedWidthDeserializeErrorHandler? errorHandler;
    private readonly HashSet<string>? nullValues;
    private readonly CustomConverterDictionary? customConverters;

    private FixedWidthRecordBinder(
        CultureInfo culture,
        FixedWidthDeserializeErrorHandler? errorHandler,
        IReadOnlyList<BindingTemplate> templates,
        IReadOnlyList<string>? nullValues = null,
        CustomConverterDictionary? customConverters = null)
    {
        this.culture = culture;
        this.errorHandler = errorHandler;
        this.nullValues = nullValues is { Count: > 0 } ? new HashSet<string>(nullValues, StringComparer.Ordinal) : null;
        this.customConverters = customConverters;
        bindings = InstantiateBindings(templates);
    }

    /// <summary>
    /// Creates a reflection-based binder for types without [FixedWidthGenerateBinder].
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based binding may not work with trimming. Use [FixedWidthGenerateBinder] attribute for AOT/trimming support.")]
    [RequiresDynamicCode("Reflection-based binding requires dynamic code. Use [FixedWidthGenerateBinder] attribute for AOT support.")]
    public static FixedWidthRecordBinder<T> Create(
        CultureInfo? culture,
        FixedWidthDeserializeErrorHandler? errorHandler,
        IReadOnlyList<string>? nullValues = null,
        CustomConverterDictionary? customConverters = null)
    {
        return CreateFromTemplates(culture, errorHandler, CreateTemplatesFromReflection(), nullValues, customConverters);
    }

    /// <summary>
    /// Creates a binder from pre-built templates (used by source generator).
    /// </summary>
    public static FixedWidthRecordBinder<T> CreateFromTemplates(
        CultureInfo? culture,
        FixedWidthDeserializeErrorHandler? errorHandler,
        IReadOnlyList<BindingTemplate> templates,
        IReadOnlyList<string>? nullValues = null,
        CustomConverterDictionary? customConverters = null)
    {
        return new FixedWidthRecordBinder<T>(
            culture ?? CultureInfo.InvariantCulture,
            errorHandler,
            templates,
            nullValues,
            customConverters);
    }

    /// <summary>
    /// Binds a fixed-width row to a record instance.
    /// </summary>
    /// <param name="row">The row to bind.</param>
    /// <returns>The bound record, or null if the row should be skipped.</returns>
    public T? Bind(FixedWidthCharSpanRow row)
    {
        var instance = new T();

        foreach (var binding in bindings)
        {
            var column = row.GetField(
                binding.Start,
                binding.Length,
                binding.PadChar,
                binding.Alignment);

            // Check if value matches null values list (only allocate string when nullValues is configured)
            if (nullValues is not null)
            {
                if (IsNullValue(column.CharSpan))
                {
                    binding.SetValue(instance, null);
                    continue;
                }
            }

            if (!binding.TryConvert(column, out var value))
            {
                // Only allocate string in error path (rare case)
                var rawValue = column.ToString();

                if (errorHandler is not null)
                {
                    var context = new FixedWidthErrorContext
                    {
                        RecordNumber = row.RecordNumber,
                        SourceLineNumber = row.SourceLineNumber,
                        FieldName = binding.MemberName,
                        RawValue = rawValue,
                        TargetType = binding.TargetType
                    };

                    var action = errorHandler(context, new FormatException(
                        $"Failed to convert field '{binding.MemberName}' value '{rawValue}' to {binding.TargetType.Name}."));

                    switch (action)
                    {
                        case FixedWidthDeserializeErrorAction.SkipRecord:
                            return null;
                        case FixedWidthDeserializeErrorAction.Throw:
                        default:
                            break;
                    }
                }

                throw new FixedWidthException(
                    FixedWidthErrorCode.ParseError,
                    $"Failed to convert field '{binding.MemberName}' value '{rawValue}' to {binding.TargetType.Name}.",
                    row.RecordNumber,
                    row.SourceLineNumber);
            }

            binding.SetValue(instance, value);
        }

        return instance;
    }

    /// <summary>
    /// Binds a fixed-width row into an existing record instance.
    /// </summary>
    /// <param name="instance">The existing instance to bind into.</param>
    /// <param name="row">The row to bind.</param>
    /// <returns>True if binding succeeded, false if the row should be skipped.</returns>
    public bool BindInto(T instance, FixedWidthCharSpanRow row)
    {
        foreach (var binding in bindings)
        {
            var column = row.GetField(
                binding.Start,
                binding.Length,
                binding.PadChar,
                binding.Alignment);

            // Check if value matches null values list
            if (nullValues is not null && IsNullValue(column.CharSpan))
            {
                binding.SetValue(instance, null);
                continue;
            }

            if (!binding.TryConvert(column, out var value))
            {
                var rawValue = column.ToString();

                if (errorHandler is not null)
                {
                    var context = new FixedWidthErrorContext
                    {
                        RecordNumber = row.RecordNumber,
                        SourceLineNumber = row.SourceLineNumber,
                        FieldName = binding.MemberName,
                        RawValue = rawValue,
                        TargetType = binding.TargetType
                    };

                    var action = errorHandler(context, new FormatException(
                        $"Failed to convert field '{binding.MemberName}' value '{rawValue}' to {binding.TargetType.Name}."));

                    if (action == FixedWidthDeserializeErrorAction.SkipRecord)
                        return false;
                }

                throw new FixedWidthException(
                    FixedWidthErrorCode.ParseError,
                    $"Failed to convert field '{binding.MemberName}' value '{rawValue}' to {binding.TargetType.Name}.",
                    row.RecordNumber,
                    row.SourceLineNumber);
            }

            binding.SetValue(instance, value);
        }

        return true;
    }

    /// <summary>
    /// Checks if the span matches any configured null value using span comparison (no allocation).
    /// </summary>
    private bool IsNullValue(ReadOnlySpan<char> span)
    {
        if (nullValues is null)
            return false;

        foreach (var nullValue in nullValues)
        {
            if (span.SequenceEqual(nullValue.AsSpan()))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Binds all rows from a reader to records.
    /// </summary>
    /// <remarks>
    /// This method collects all records in memory since the ref struct reader
    /// cannot be used with iterators.
    /// </remarks>
    public static List<T> Bind(
        FixedWidthCharSpanReader reader,
        CultureInfo? culture,
        FixedWidthDeserializeErrorHandler? errorHandler,
        IReadOnlyList<string>? nullValues = null,
        CustomConverterDictionary? customConverters = null,
        IProgress<FixedWidthProgress>? progress = null,
        int progressIntervalRows = 1000)
    {
        // Estimate capacity to avoid List resizing allocations
        var estimatedCapacity = reader.EstimateRowCount();

        // Prefer descriptor binder for boxing-free parsing when no error handler is needed
        // Only use descriptor binder when no custom converters are specified
        if (errorHandler is null && customConverters is null &&
            FixedWidthRecordBinderFactory.TryCreateDescriptorBinder<T>(culture, nullValues, out var descriptorBinder))
        {
            return BindWithTypedBinder(reader, descriptorBinder!, estimatedCapacity, progress, progressIntervalRows);
        }

        // Fall back to generic binder
        var binder = Create(culture, errorHandler, nullValues, customConverters);
        var results = new List<T>(estimatedCapacity);
        int recordsProcessed = 0;

        foreach (var row in reader)
        {
            var record = binder.Bind(row);
            if (record is not null)
            {
                results.Add(record);
            }

            recordsProcessed++;

            // Report progress
            if (progress is not null && recordsProcessed % progressIntervalRows == 0)
            {
                progress.Report(new FixedWidthProgress
                {
                    RecordsProcessed = recordsProcessed
                });
            }
        }

        // Report final progress
        progress?.Report(new FixedWidthProgress
        {
            RecordsProcessed = recordsProcessed
        });

        return results;
    }

    private static List<T> BindWithTypedBinder(
        FixedWidthCharSpanReader reader,
        IFixedWidthBinder<T> binder,
        int estimatedCapacity,
        IProgress<FixedWidthProgress>? progress,
        int progressIntervalRows)
    {
        var results = new List<T>(estimatedCapacity);
        int recordsProcessed = 0;

        foreach (var row in reader)
        {
            var record = binder.Bind(row);
            if (record is not null)
            {
                results.Add(record);
            }

            recordsProcessed++;

            // Report progress
            if (progress is not null && recordsProcessed % progressIntervalRows == 0)
            {
                progress.Report(new FixedWidthProgress
                {
                    RecordsProcessed = recordsProcessed
                });
            }
        }

        // Report final progress
        progress?.Report(new FixedWidthProgress
        {
            RecordsProcessed = recordsProcessed
        });

        return results;
    }

    /// <summary>
    /// Iterates over all records, calling the callback for each one.
    /// Reuses a single record instance to minimize allocations.
    /// </summary>
    /// <remarks>
    /// This method allocates only ONE record object which is reused for each row.
    /// String properties still allocate new strings per row.
    /// For true zero-allocation, use the span-based row API directly.
    /// WARNING: The callback receives the same instance each time - copy data if needed.
    /// </remarks>
    public static void ForEach(
        FixedWidthCharSpanReader reader,
        CultureInfo? culture,
        IReadOnlyList<string>? nullValues,
        CustomConverterDictionary? customConverters,
        Action<T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        // Try descriptor binder for best performance (only when no custom converters)
        if (customConverters is null &&
            FixedWidthRecordBinderFactory.TryCreateDescriptorBinder<T>(culture, nullValues, out var descriptorBinder))
        {
            ForEachWithTypedBinder(reader, descriptorBinder!, callback);
            return;
        }

        // Fall back to generic binder
        var binder = Create(culture, null, nullValues, customConverters);
        var instance = new T();

        foreach (var row in reader)
        {
            // Re-bind into the same instance
            foreach (var binding in binder.bindings)
            {
                var column = row.GetField(
                    binding.Start,
                    binding.Length,
                    binding.PadChar,
                    binding.Alignment);

                // Check if value matches null values list
                if (binder.nullValues is not null && binder.IsNullValue(column.CharSpan))
                {
                    binding.SetValue(instance, null);
                    continue;
                }

                if (!binding.TryConvert(column, out var value))
                {
                    var rawValue = column.ToString();
                    throw new FixedWidthException(
                        FixedWidthErrorCode.ParseError,
                        $"Failed to convert field '{binding.MemberName}' value '{rawValue}' to {binding.TargetType.Name}.",
                        row.RecordNumber,
                        row.SourceLineNumber);
                }

                binding.SetValue(instance, value);
            }

            callback(instance);
        }
    }

    private static void ForEachWithTypedBinder(
        FixedWidthCharSpanReader reader,
        IFixedWidthBinder<T> binder,
        Action<T> callback)
    {
        var instance = new T();

        foreach (var row in reader)
        {
            if (binder.BindInto(instance, row))
            {
                callback(instance);
            }
        }
    }

    private List<FieldBinding> InstantiateBindings(IReadOnlyList<BindingTemplate> templates)
    {
        var list = new List<FieldBinding>(templates.Count);

        foreach (var template in templates)
        {
            var converter = ConverterFactory.CreateConverter(
                template.TargetType,
                culture,
                template.Format,
                customConverters);
            // Wrap typed setter in untyped delegate
            var setter = new SetterWrapper<T>(template.Setter);

            list.Add(new FieldBinding(
                template.MemberName,
                template.TargetType,
                template.Start,
                template.Length,
                template.PadChar,
                template.Alignment,
                converter,
                setter.Set));
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
            .Select(p => (Property: p, Attribute: p.GetCustomAttribute<FixedWidthColumnAttribute>()))
            .Where(x => x.Attribute is not null)
            .OrderBy(x => x.Attribute!.Start);

        var bindings = new List<BindingTemplate>();

        foreach (var (property, attribute) in members)
        {
            var setter = SetterFactory.CreateSetter(property);

            bindings.Add(new BindingTemplate(
                property.Name,
                property.PropertyType,
                attribute!.Start,
                attribute.Length,
                attribute.PadChar == '\0' ? ' ' : attribute.PadChar,
                attribute.Alignment,
                attribute.Format,
                setter));
        }

        return bindings;
    }

    /// <summary>
    /// Template for creating field bindings. Used by source generator and reflection.
    /// </summary>
    public sealed record BindingTemplate(
        string MemberName,
        Type TargetType,
        int Start,
        int Length,
        char PadChar,
        FieldAlignment Alignment,
        string? Format,
        Action<T, object?> Setter);
}

/// <summary>
/// Runtime binding information for a single field.
/// </summary>
internal sealed class FieldBinding(
    string memberName,
    Type targetType,
    int start,
    int length,
    char padChar,
    FieldAlignment alignment,
    ColumnConverter converter,
    Action<object, object?> setter)
{
    public string MemberName { get; } = memberName;
    public Type TargetType { get; } = targetType;
    public int Start { get; } = start;
    public int Length { get; } = length;
    public char PadChar { get; } = padChar;
    public FieldAlignment Alignment { get; } = alignment;
    private readonly ColumnConverter converter = converter;
    private readonly Action<object, object?> setter = setter;

    public bool TryConvert(FixedWidthCharSpanColumn column, out object? value)
        => converter(column.CharSpan, out value);

    public void SetValue(object instance, object? value)
        => setter(instance, value);
}

/// <summary>
/// Delegate for converting a column span to a typed value.
/// </summary>
internal delegate bool ColumnConverter(ReadOnlySpan<char> span, out object? value);

/// <summary>
/// Factory for creating property setters.
/// </summary>
internal static class SetterFactory
{
    public static Action<object, object?> CreateSetter(PropertyInfo property) => property.SetValue;
}

/// <summary>
/// Wraps a typed setter to provide an untyped interface.
/// </summary>
internal sealed class SetterWrapper<T>(Action<T, object?> typedSetter) where T : class
{
    public void Set(object obj, object? value) => typedSetter((T)obj, value);
}

/// <summary>
/// Factory for creating type converters.
/// </summary>
internal static class ConverterFactory
{
    public static ColumnConverter CreateConverter(
        Type targetType,
        CultureInfo culture,
        string? format,
        CustomConverterDictionary? customConverters = null)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        var isNullable = underlyingType is not null;
        var actualType = underlyingType ?? targetType;

        // Check for custom converter first (takes precedence over built-in converters)
        if (customConverters is not null && customConverters.TryGetValue(actualType, out var customConverter))
        {
            var customWrapper = new CustomConverterWrapper(customConverter, culture, format);
            if (isNullable)
            {
                return new NullableWrapper(customWrapper.Convert).Convert;
            }
            return customWrapper.Convert;
        }

        ColumnConverter baseConverter = actualType switch
        {
            _ when actualType == typeof(string) => ConvertToString,
            _ when actualType == typeof(int) => new Int32Converter(culture).Convert,
            _ when actualType == typeof(long) => new Int64Converter(culture).Convert,
            _ when actualType == typeof(short) => new Int16Converter(culture).Convert,
            _ when actualType == typeof(byte) => new ByteConverter(culture).Convert,
            _ when actualType == typeof(decimal) => new DecimalConverter(culture).Convert,
            _ when actualType == typeof(double) => new DoubleConverter(culture).Convert,
            _ when actualType == typeof(float) => new SingleConverter(culture).Convert,
            _ when actualType == typeof(bool) => ConvertToBoolean,
            _ when actualType == typeof(DateTime) => new DateTimeConverter(culture, format).Convert,
            _ when actualType == typeof(DateTimeOffset) => new DateTimeOffsetConverter(culture, format).Convert,
            _ when actualType == typeof(DateOnly) => new DateOnlyConverter(culture, format).Convert,
            _ when actualType == typeof(TimeOnly) => new TimeOnlyConverter(culture, format).Convert,
            _ when actualType == typeof(Guid) => ConvertToGuid,
            _ when actualType.IsEnum => new EnumConverter(actualType).Convert,
            _ => throw new NotSupportedException($"Type '{targetType}' is not supported for fixed-width binding. " +
                $"Register a custom converter using .RegisterConverter<{actualType.Name}>().")
        };

        // Wrap for nullable types
        if (isNullable)
        {
            return new NullableWrapper(baseConverter).Convert;
        }

        return baseConverter;
    }

    /// <summary>
    /// Wrapper for custom converters to adapt them to the ColumnConverter signature.
    /// </summary>
    private sealed class CustomConverterWrapper(
        InternalFixedWidthConverter converter,
        CultureInfo culture,
        string? format)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
            => converter(span, culture, format, out value);
    }

    private static bool ConvertToString(ReadOnlySpan<char> span, out object? value)
    {
        value = new string(span);
        return true;
    }

    private static bool ConvertToBoolean(ReadOnlySpan<char> span, out object? value)
    {
        if (bool.TryParse(span, out var result))
        {
            value = result;
            return true;
        }

        // Also support common variations
        if (span.Length == 1)
        {
            var c = span[0];
            if (c is '1' or 'Y' or 'y' or 'T' or 't')
            {
                value = true;
                return true;
            }
            if (c is '0' or 'N' or 'n' or 'F' or 'f')
            {
                value = false;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool ConvertToGuid(ReadOnlySpan<char> span, out object? value)
    {
        if (Guid.TryParse(span, out var result))
        {
            value = result;
            return true;
        }
        value = null;
        return false;
    }

    private sealed class NullableWrapper(ColumnConverter baseConverter)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (span.IsEmpty || span.IsWhiteSpace())
            {
                value = null;
                return true;
            }
            return baseConverter(span, out value);
        }
    }

    private sealed class Int32Converter(CultureInfo culture)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (int.TryParse(span, NumberStyles.Integer, culture, out var result))
            {
                value = result;
                return true;
            }
            value = null;
            return false;
        }
    }

    private sealed class Int64Converter(CultureInfo culture)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (long.TryParse(span, NumberStyles.Integer, culture, out var result))
            {
                value = result;
                return true;
            }
            value = null;
            return false;
        }
    }

    private sealed class Int16Converter(CultureInfo culture)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (short.TryParse(span, NumberStyles.Integer, culture, out var result))
            {
                value = result;
                return true;
            }
            value = null;
            return false;
        }
    }

    private sealed class ByteConverter(CultureInfo culture)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (byte.TryParse(span, NumberStyles.Integer, culture, out var result))
            {
                value = result;
                return true;
            }
            value = null;
            return false;
        }
    }

    private sealed class DecimalConverter(CultureInfo culture)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (decimal.TryParse(span, NumberStyles.Number, culture, out var result))
            {
                value = result;
                return true;
            }
            value = null;
            return false;
        }
    }

    private sealed class DoubleConverter(CultureInfo culture)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (double.TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var result))
            {
                value = result;
                return true;
            }
            value = null;
            return false;
        }
    }

    private sealed class SingleConverter(CultureInfo culture)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (float.TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var result))
            {
                value = result;
                return true;
            }
            value = null;
            return false;
        }
    }

    private sealed class DateTimeConverter(CultureInfo culture, string? format)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (format is not null)
            {
                if (DateTime.TryParseExact(span, format, culture, DateTimeStyles.None, out var exactResult))
                {
                    value = exactResult;
                    return true;
                }
            }
            else if (DateTime.TryParse(span, culture, DateTimeStyles.None, out var result))
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }
    }

    private sealed class DateTimeOffsetConverter(CultureInfo culture, string? format)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (format is not null)
            {
                if (DateTimeOffset.TryParseExact(span, format, culture, DateTimeStyles.None, out var exactResult))
                {
                    value = exactResult;
                    return true;
                }
            }
            else if (DateTimeOffset.TryParse(span, culture, DateTimeStyles.None, out var result))
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }
    }

    private sealed class DateOnlyConverter(CultureInfo culture, string? format)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (format is not null)
            {
                if (DateOnly.TryParseExact(span, format, culture, DateTimeStyles.None, out var exactResult))
                {
                    value = exactResult;
                    return true;
                }
            }
            else if (DateOnly.TryParse(span, culture, DateTimeStyles.None, out var result))
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }
    }

    private sealed class TimeOnlyConverter(CultureInfo culture, string? format)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (format is not null)
            {
                if (TimeOnly.TryParseExact(span, format, culture, DateTimeStyles.None, out var exactResult))
                {
                    value = exactResult;
                    return true;
                }
            }
            else if (TimeOnly.TryParse(span, culture, DateTimeStyles.None, out var result))
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }
    }

    private sealed class EnumConverter(Type enumType)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            // Try numeric first
            if (int.TryParse(span, out var intValue))
            {
                if (Enum.IsDefined(enumType, intValue))
                {
                    value = Enum.ToObject(enumType, intValue);
                    return true;
                }
            }

            // Try by name (case-insensitive)
            var name = new string(span);
            if (Enum.TryParse(enumType, name, ignoreCase: true, out var result))
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }
    }
}
