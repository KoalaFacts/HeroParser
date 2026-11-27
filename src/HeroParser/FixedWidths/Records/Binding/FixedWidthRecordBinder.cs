using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records;
using HeroParser.Internal;
using HeroParser.Validation;
using CustomConverterDictionary = System.Collections.Generic.IReadOnlyDictionary<System.Type, HeroParser.FixedWidths.Records.InternalFixedWidthConverter>;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Binds fixed-width row data to typed record instances.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
internal sealed class FixedWidthRecordBinder<T> : IFixedWidthSourceBinder<T> where T : new()
{
    private static readonly ConcurrentDictionary<Type, List<BindingTemplate>> bindingCache = new();

    private readonly IReadOnlyList<FieldBinding> bindings;
    private readonly CultureInfo culture;
    private readonly FixedWidthDeserializeErrorHandler? errorHandler;
    private readonly HashSet<string>? nullValues;
    private readonly CustomConverterDictionary? customConverters;

    [RequiresUnreferencedCode("Reflection-based binding may not work with trimming. Use [GenerateBinder] attribute for AOT/trimming support.")]
    [RequiresDynamicCode("Reflection-based binding requires dynamic code. Use [GenerateBinder] attribute for AOT support.")]
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
    /// Creates a reflection-based binder for types without [GenerateBinder].
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based binding may not work with trimming. Use [GenerateBinder] attribute for AOT/trimming support.")]
    [RequiresDynamicCode("Reflection-based binding requires dynamic code. Use [GenerateBinder] attribute for AOT support.")]
    public static FixedWidthRecordBinder<T> Create(
        CultureInfo? culture,
        FixedWidthDeserializeErrorHandler? errorHandler,
        IReadOnlyList<string>? nullValues = null,
        CustomConverterDictionary? customConverters = null)
    {
        var templates = CreateTemplatesFromReflection();
        var layouts = new FixedWidthFieldLayout[templates.Count];
        for (int i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            layouts[i] = new FixedWidthFieldLayout(template.MemberName, template.Start, template.Length);
        }
        new FixedWidthFieldLayoutValidator(layouts).Validate();

        return new FixedWidthRecordBinder<T>(
            culture ?? CultureInfo.InvariantCulture,
            errorHandler,
            templates,
            nullValues,
            customConverters);
    }

    /// <summary>
    /// Binds a fixed-width row to a new record instance.
    /// </summary>
    /// <param name="row">The row to bind.</param>
    /// <param name="result">The bound record when successful.</param>
    /// <param name="errors">Optional list to collect validation errors. Not used by the reflection-based binder.</param>
    /// <returns>True if binding succeeded; otherwise false if the row should be skipped.</returns>
    public bool TryBind(FixedWidthCharSpanRow row, out T result, List<ValidationError>? errors = null)
    {
        var instance = new T();
        if (!BindInto(ref instance, row, errors))
        {
            result = default!;
            return false;
        }

        result = instance;
        return true;
    }

    /// <summary>
    /// Binds a fixed-width row into an existing record instance.
    /// </summary>
    /// <param name="instance">The existing instance to bind into.</param>
    /// <param name="row">The row to bind.</param>
    /// <param name="errors">Optional list to collect validation errors. Not used by the reflection-based binder.</param>
    /// <returns>True if binding succeeded, false if the row should be skipped.</returns>
    public bool BindInto(ref T instance, FixedWidthCharSpanRow row, List<ValidationError>? errors = null)
    {
        if (typeof(T).IsValueType)
        {
            object boxed = instance!;
            var success = BindIntoObject(boxed, row);
            if (success)
            {
                instance = (T)boxed;
            }
            return success;
        }

        if (instance is null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        return BindIntoObject(instance!, row);
    }

    private bool BindIntoObject(object instance, FixedWidthCharSpanRow row)
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
    /// cannot be used with iterators. Prefers the source-generated descriptor
    /// binder for <typeparamref name="T"/>; falls back to the reflection-based
    /// <see cref="Create"/> when options require error callbacks or custom converters,
    /// or when no descriptor binder is registered. The generator preserves public
    /// property metadata for the options path.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members attributed with RequiresUnreferencedCode may break when trimming",
        Justification = "Generated records preserve public property metadata for error-handler and custom-converter reflection fallback. Setters use PropertyInfo.SetValue, not runtime code generation. Ungenerated records require preserved reflection metadata.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
        Justification = "Generated records preserve public property metadata for error-handler and custom-converter reflection fallback. Setters use PropertyInfo.SetValue, not runtime code generation. Ungenerated records require preserved reflection metadata.")]
    public static FixedWidthReadResult<T> Bind(
        FixedWidthCharSpanReader reader,
        CultureInfo? culture,
        FixedWidthDeserializeErrorHandler? errorHandler,
        IReadOnlyList<string>? nullValues = null,
        CustomConverterDictionary? customConverters = null,
        IProgress<FixedWidthProgress>? progress = null,
        int progressIntervalRows = 1000,
        ValidationMode validationMode = ValidationMode.Strict)
    {
        // Estimate capacity to avoid List resizing allocations
        var estimatedCapacity = reader.EstimateRowCount();

        // Prefer generated binder for boxing-free parsing + validation support
        if (errorHandler is null && customConverters is null &&
            FixedWidthRecordBinderFactory.TryCreateGeneratedBinder<T>(culture, nullValues, out var generatedBinder))
        {
            var errors = new List<ValidationError>();
            var records = BindWithTypedBinder(reader, generatedBinder!, estimatedCapacity, progress, progressIntervalRows, errors);
            return new FixedWidthReadResult<T>(records, errors, validationMode);
        }

        // Fall back to descriptor binder when no error handler and no custom converters
        if (errorHandler is null && customConverters is null &&
            FixedWidthRecordBinderFactory.TryCreateDescriptorBinder<T>(culture, nullValues, out var descriptorBinder))
        {
            var records = BindWithTypedBinder(reader, descriptorBinder!, estimatedCapacity, progress, progressIntervalRows, null);
            return new FixedWidthReadResult<T>(records, [], validationMode);
        }

        // Fall back to reflection-based binder
        var reflectionBinder = Create(culture, errorHandler, nullValues, customConverters);
        var resultList = new List<T>(estimatedCapacity);
        int recordsProcessed = 0;

        foreach (var row in reader)
        {
            if (reflectionBinder.TryBind(row, out var record))
            {
                resultList.Add(record);
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

        return new FixedWidthReadResult<T>(resultList, [], validationMode);
    }

    /// <summary>
    /// Binds records using an externally-provided binder (e.g., from a fluent map).
    /// </summary>
    public static FixedWidthReadResult<T> Bind(
        FixedWidthCharSpanReader reader,
        IFixedWidthSourceBinder<T> binder,
        IProgress<FixedWidthProgress>? progress = null,
        int progressIntervalRows = 1000,
        ValidationMode validationMode = ValidationMode.Strict)
    {
        var errors = new List<ValidationError>();
        var records = BindWithTypedBinder(reader, binder, reader.EstimateRowCount(),
            progress, progressIntervalRows, errors);
        return new FixedWidthReadResult<T>(records, errors, validationMode);
    }

    /// <summary>
    /// Binds records from pipe-backed fixed-width rows asynchronously.
    /// </summary>
    internal static async IAsyncEnumerable<T> BindAsync(
        FixedWidthPipeSequenceReader reader,
        FixedWidthReadOptions options,
        Encoding encoding,
        CultureInfo? culture,
        FixedWidthDeserializeErrorHandler? errorHandler,
        IReadOnlyList<string>? nullValues = null,
        CustomConverterDictionary? customConverters = null,
        IProgress<FixedWidthProgress>? progress = null,
        int progressIntervalRows = 1000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (errorHandler is null &&
            customConverters is null &&
            Utf8SpanParser.IsUtf8Encoding(encoding) &&
            FixedWidthRecordBinderFactory.TryCreateGeneratedByteBinder<T>(culture, nullValues, out var generatedByteBinder))
        {
            await foreach (var record in BindAsync(
                reader,
                generatedByteBinder!,
                progress,
                progressIntervalRows,
                cancellationToken).ConfigureAwait(false))
            {
                yield return record;
            }

            yield break;
        }

        var binder =
            errorHandler is null && customConverters is null &&
            FixedWidthRecordBinderFactory.TryCreateGeneratedBinder<T>(culture, nullValues, out var generatedBinder)
                ? generatedBinder!
                : errorHandler is null && customConverters is null &&
                  FixedWidthRecordBinderFactory.TryCreateDescriptorBinder<T>(culture, nullValues, out var descriptorBinder)
                    ? descriptorBinder!
                    : Create(culture, errorHandler, nullValues, customConverters);

        await foreach (var record in BindAsync(
            reader,
            binder,
            options,
            encoding,
            progress,
            progressIntervalRows,
            cancellationToken).ConfigureAwait(false))
        {
            yield return record;
        }
    }

    /// <summary>
    /// Binds records from borrowed pipe-backed fixed-width rows asynchronously using the provided binder.
    /// </summary>
    internal static async IAsyncEnumerable<T> BindAsync(
        FixedWidthPipeSequenceReader reader,
        IFixedWidthByteSourceBinder<T> binder,
        IProgress<FixedWidthProgress>? progress = null,
        int progressIntervalRows = 1000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(binder);

        int recordsProcessed = 0;
        byte[]? contiguousRowBuffer = null;

        try
        {
            while (await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = reader.Current;

                if (row.TryGetContiguousRow(out var contiguousRow))
                {
                    if (binder.TryBind(contiguousRow, out var record, errors: null))
                    {
                        yield return record;
                    }
                }
                else
                {
                    int rowLength = row.Length;
                    if (contiguousRowBuffer is null || contiguousRowBuffer.Length < rowLength)
                    {
                        if (contiguousRowBuffer is not null)
                        {
                            ArrayPool<byte>.Shared.Return(contiguousRowBuffer);
                        }

                        contiguousRowBuffer = ArrayPool<byte>.Shared.Rent(rowLength);
                    }

                    row.CopyTo(contiguousRowBuffer);
                    if (binder.TryBind(row.CreateContiguousRow(contiguousRowBuffer.AsSpan(0, rowLength)), out var record, errors: null))
                    {
                        yield return record;
                    }
                }

                recordsProcessed++;

                if (progress is not null && recordsProcessed % progressIntervalRows == 0)
                {
                    progress.Report(new FixedWidthProgress
                    {
                        RecordsProcessed = recordsProcessed
                    });
                }
            }
        }
        finally
        {
            if (contiguousRowBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(contiguousRowBuffer);
            }
        }

        progress?.Report(new FixedWidthProgress
        {
            RecordsProcessed = recordsProcessed
        });
    }

    /// <summary>
    /// Binds records from borrowed pipe-backed fixed-width rows asynchronously using the provided binder.
    /// </summary>
    internal static async IAsyncEnumerable<T> BindAsync(
        FixedWidthPipeSequenceReader reader,
        IFixedWidthSourceBinder<T> binder,
        FixedWidthReadOptions options,
        Encoding encoding,
        IProgress<FixedWidthProgress>? progress = null,
        int progressIntervalRows = 1000,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(encoding);

        if (Utf8SpanParser.IsUtf8Encoding(encoding) && binder is IFixedWidthByteSourceBinder<T> byteBinder)
        {
            await foreach (var record in BindAsync(
                reader,
                byteBinder,
                progress,
                progressIntervalRows,
                cancellationToken).ConfigureAwait(false))
            {
                yield return record;
            }

            yield break;
        }

        int recordsProcessed = 0;
        byte[]? contiguousRowBuffer = null;
        char[]? decodedBuffer = null;

        try
        {
            while (await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = reader.Current;

                ReadOnlySpan<byte> rawRecord;
                if (row.TryGetContiguousRow(out var contiguousRow))
                {
                    rawRecord = contiguousRow.RawRecord;
                }
                else
                {
                    int rowLength = row.Length;
                    if (contiguousRowBuffer is null || contiguousRowBuffer.Length < rowLength)
                    {
                        if (contiguousRowBuffer is not null)
                        {
                            ArrayPool<byte>.Shared.Return(contiguousRowBuffer);
                        }

                        contiguousRowBuffer = ArrayPool<byte>.Shared.Rent(rowLength);
                    }

                    row.CopyTo(contiguousRowBuffer);
                    rawRecord = contiguousRowBuffer.AsSpan(0, rowLength);
                }

                int charCount = encoding.GetCharCount(rawRecord);
                if (decodedBuffer is null || decodedBuffer.Length < charCount)
                {
                    if (decodedBuffer is not null)
                    {
                        ArrayPool<char>.Shared.Return(decodedBuffer);
                    }

                    decodedBuffer = ArrayPool<char>.Shared.Rent(charCount);
                }

                int charsWritten = encoding.GetChars(rawRecord, decodedBuffer);
                var decodedRow = new FixedWidthCharSpanRow(
                    decodedBuffer.AsSpan(0, charsWritten),
                    row.RecordNumber,
                    row.SourceLineNumber,
                    options);

                if (binder.TryBind(decodedRow, out var record, errors: null))
                {
                    yield return record;
                }

                recordsProcessed++;

                if (progress is not null && recordsProcessed % progressIntervalRows == 0)
                {
                    progress.Report(new FixedWidthProgress
                    {
                        RecordsProcessed = recordsProcessed
                    });
                }
            }
        }
        finally
        {
            if (contiguousRowBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(contiguousRowBuffer);
            }

            if (decodedBuffer is not null)
            {
                ArrayPool<char>.Shared.Return(decodedBuffer);
            }
        }

        progress?.Report(new FixedWidthProgress
        {
            RecordsProcessed = recordsProcessed
        });
    }


    private static List<T> BindWithTypedBinder(
        FixedWidthCharSpanReader reader,
        IFixedWidthSourceBinder<T> binder,
        int estimatedCapacity,
        IProgress<FixedWidthProgress>? progress,
        int progressIntervalRows,
        List<ValidationError>? errors)
    {
        var results = new List<T>(estimatedCapacity);
        int recordsProcessed = 0;

        foreach (var row in reader)
        {
            if (binder.TryBind(row, out var record, errors))
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

        // Try generated binder first (supports validation + best performance)
        if (customConverters is null &&
            FixedWidthRecordBinderFactory.TryCreateGeneratedBinder<T>(culture, nullValues, out var generatedBinder))
        {
            ForEachWithTypedBinder(reader, generatedBinder!, callback);
            return;
        }

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
            if (binder.BindInto(ref instance, row))
            {
                callback(instance);
            }
        }
    }

    private static void ForEachWithTypedBinder(
        FixedWidthCharSpanReader reader,
        IFixedWidthSourceBinder<T> binder,
        Action<T> callback)
    {
        var instance = new T();

        foreach (var row in reader)
        {
            if (binder.BindInto(ref instance, row))
            {
                callback(instance);
            }
        }
    }

    [RequiresUnreferencedCode("Uses runtime-discovered converters which may require reflection for enum and generic ISpanParsable binding.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for generic ISpanParsable converters.")]
    private List<FieldBinding> InstantiateBindings(IReadOnlyList<BindingTemplate> templates)
    {
        var list = new List<FieldBinding>(templates.Count);

        foreach (var template in templates)
        {
            var factory = new ConverterFactory(culture, customConverters);
            var converter = factory.CreateConverter(template.TargetType, template.Format);
            list.Add(new FieldBinding(
                template.MemberName,
                template.TargetType,
                template.Start,
                template.Length,
                template.PadChar,
                template.Alignment,
                converter,
                template.Setter));
        }

        return list;
    }

    [RequiresUnreferencedCode("Uses reflection over T.GetProperties; only reached via the [RequiresUnreferencedCode] Create method.")]
    [RequiresDynamicCode("Uses runtime-discovered property setters and converters; only reached via the [RequiresDynamicCode] Create method.")]
    private static IReadOnlyList<BindingTemplate> CreateTemplatesFromReflection()
        => bindingCache.GetOrAdd(typeof(T), _ => BuildTemplates());

    [RequiresUnreferencedCode("Uses reflection over T.GetProperties; only reached via the [RequiresUnreferencedCode] Create method.")]
    [RequiresDynamicCode("Uses runtime-discovered property setters and converters; only reached via the [RequiresDynamicCode] Create method.")]
    private static List<BindingTemplate> BuildTemplates()
    {
        var members = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod is { IsStatic: false })
            .Select(p => (Property: p, Attribute: p.GetCustomAttribute<PositionalMapAttribute>()))
            .Where(x => x.Attribute is not null)
            .OrderBy(x => x.Attribute!.Start);

        var bindings = new List<BindingTemplate>();

        foreach (var (property, attribute) in members)
        {
            Action<object, object?> setter = property.SetValue;
            var parseAttr = property.GetCustomAttribute<ParseAttribute>();

            bindings.Add(new BindingTemplate(
                property.Name,
                property.PropertyType,
                attribute!.Start,
                attribute.Length,
                attribute.PadChar == '\0' ? ' ' : attribute.PadChar,
                attribute.Alignment,
                parseAttr?.Format,
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
        Action<object, object?> Setter);
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
/// Factory for creating type converters.
/// </summary>
internal readonly struct ConverterFactory(CultureInfo culture, CustomConverterDictionary? customConverters)
{
    [RequiresUnreferencedCode("Uses MakeGenericMethod for generic ISpanParsable<T> converters.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for generic ISpanParsable<T> converters.")]
    public ColumnConverter CreateConverter(Type targetType, string? format)
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
            _ when actualType == typeof(uint) => new NumberConverter<uint>(culture, NumberStyles.Integer).Convert,
            _ when actualType == typeof(ulong) => new NumberConverter<ulong>(culture, NumberStyles.Integer).Convert,
            _ when actualType == typeof(ushort) => new NumberConverter<ushort>(culture, NumberStyles.Integer).Convert,
            _ when actualType == typeof(sbyte) => new NumberConverter<sbyte>(culture, NumberStyles.Integer).Convert,
            _ when actualType == typeof(decimal) => new DecimalConverter(culture).Convert,
            _ when actualType == typeof(double) => new DoubleConverter(culture).Convert,
            _ when actualType == typeof(float) => new SingleConverter(culture).Convert,
            _ when actualType == typeof(Half) => new NumberConverter<Half>(culture, NumberStyles.Float | NumberStyles.AllowThousands).Convert,
            _ when actualType == typeof(Int128) => new NumberConverter<Int128>(culture, NumberStyles.Integer).Convert,
            _ when actualType == typeof(UInt128) => new NumberConverter<UInt128>(culture, NumberStyles.Integer).Convert,
            _ when actualType == typeof(BigInteger) => new NumberConverter<BigInteger>(culture, NumberStyles.Integer).Convert,
            _ when actualType == typeof(char) => ConvertToChar,
            _ when actualType == typeof(bool) => ConvertToBoolean,
            _ when actualType == typeof(DateTime) => new DateTimeConverter(culture, format).Convert,
            _ when actualType == typeof(DateTimeOffset) => new DateTimeOffsetConverter(culture, format).Convert,
            _ when actualType == typeof(DateOnly) => new DateOnlyConverter(culture, format).Convert,
            _ when actualType == typeof(TimeOnly) => new TimeOnlyConverter(culture, format).Convert,
            _ when actualType == typeof(TimeSpan) => new TimeSpanConverter(culture, format).Convert,
            _ when actualType == typeof(Guid) => ConvertToGuid,
            _ when actualType == typeof(Uri) => ConvertToUri,
            _ when actualType == typeof(byte[]) => ConvertToBase64Bytes,
            _ when actualType.IsEnum => new EnumConverter(actualType).Convert,
            _ when ImplementsSelfInterface(actualType, typeof(ISpanParsable<>)) => CreateSpanParsableConverter(actualType, culture),
            _ when ImplementsSelfInterface(actualType, typeof(IUtf8SpanParsable<>)) => CreateUtf8SpanParsableConverter(actualType, culture),
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
            if (new CultureNumberParser(culture).TryParse(span, NumberStyles.Integer, out int result))
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
            if (new CultureNumberParser(culture).TryParse(span, NumberStyles.Integer, out long result))
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
            if (new CultureNumberParser(culture).TryParse(span, NumberStyles.Integer, out short result))
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
            if (new CultureNumberParser(culture).TryParse(span, NumberStyles.Integer, out byte result))
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
            if (new CultureNumberParser(culture).TryParse(span, NumberStyles.Number, out decimal result))
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
            if (new CultureNumberParser(culture).TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, out double result))
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
            if (new CultureNumberParser(culture).TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, out float result))
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
            if (Enum.TryParse(enumType, span, ignoreCase: true, out var result))
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }
    }

    private static bool ConvertToChar(ReadOnlySpan<char> span, out object? value)
    {
        if (span.Length == 1)
        {
            value = span[0];
            return true;
        }
        value = null;
        return false;
    }

    private static bool ConvertToUri(ReadOnlySpan<char> span, out object? value)
    {
        if (span.IsEmpty || span.IsWhiteSpace())
        {
            value = null;
            return true;
        }
        if (Uri.TryCreate(new string(span.Trim()), UriKind.RelativeOrAbsolute, out var uri))
        {
            value = uri;
            return true;
        }
        value = null;
        return false;
    }

    private static bool ConvertToBase64Bytes(ReadOnlySpan<char> span, out object? value)
    {
        if (span.IsEmpty || span.IsWhiteSpace())
        {
            value = null;
            return true;
        }
        if (Utf8SpanParser.TryParseBase64Chars(span, out var bytes))
        {
            value = bytes;
            return true;
        }
        value = null;
        return false;
    }

    private sealed class NumberConverter<TNum>(CultureInfo culture, NumberStyles styles) where TNum : INumberBase<TNum>
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (new CultureNumberParser(culture).TryParse(span, styles, out TNum result))
            {
                value = result;
                return true;
            }
            value = null;
            return false;
        }
    }

    private sealed class TimeSpanConverter(CultureInfo culture, string? format)
    {
        public bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (format is not null)
            {
                if (TimeSpan.TryParseExact(span, format, culture, out var exactResult))
                {
                    value = exactResult;
                    return true;
                }
            }
            else if (TimeSpan.TryParse(span, culture, out var result))
            {
                value = result;
                return true;
            }

            value = null;
            return false;
        }
    }

    [RequiresUnreferencedCode("Uses Type.GetInterfaces() to detect ISpanParsable<T> and IUtf8SpanParsable<T> implementations.")]
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

    [RequiresUnreferencedCode("Uses MakeGenericMethod for ISpanParsable<T> conversion.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for ISpanParsable<T> conversion.")]
    private static ColumnConverter CreateSpanParsableConverter(Type actualType, CultureInfo culture)
    {
        var method = typeof(ConverterFactory).GetMethod(
            nameof(CreateSpanParsableGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (ColumnConverter)method.MakeGenericMethod(actualType).Invoke(null, [culture, !actualType.IsValueType])!;
    }

    private static ColumnConverter CreateSpanParsableGeneric<TValue>(CultureInfo culture, bool allowEmptyAsNull)
        where TValue : ISpanParsable<TValue>
    {
        return Convert;

        bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (allowEmptyAsNull && (span.IsEmpty || span.IsWhiteSpace()))
            {
                value = null;
                return true;
            }

            if (TValue.TryParse(span, culture, out var parsed))
            {
                value = parsed;
                return true;
            }

            value = null;
            return false;
        }
    }

    [RequiresUnreferencedCode("Uses MakeGenericMethod for IUtf8SpanParsable<T> conversion.")]
    [RequiresDynamicCode("Uses MakeGenericMethod for IUtf8SpanParsable<T> conversion.")]
    private static ColumnConverter CreateUtf8SpanParsableConverter(Type actualType, CultureInfo culture)
    {
        var method = typeof(ConverterFactory).GetMethod(
            nameof(CreateUtf8SpanParsableGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (ColumnConverter)method.MakeGenericMethod(actualType).Invoke(null, [culture, !actualType.IsValueType])!;
    }

    private static ColumnConverter CreateUtf8SpanParsableGeneric<TValue>(CultureInfo culture, bool allowEmptyAsNull)
        where TValue : IUtf8SpanParsable<TValue>
    {
        return Convert;

        bool Convert(ReadOnlySpan<char> span, out object? value)
        {
            if (allowEmptyAsNull && (span.IsEmpty || span.IsWhiteSpace()))
            {
                value = null;
                return true;
            }

            if (Utf8SpanParser.TryParseUtf8FromChars<TValue>(span, culture, out var parsed))
            {
                value = parsed;
                return true;
            }

            value = null;
            return false;
        }
    }
}
