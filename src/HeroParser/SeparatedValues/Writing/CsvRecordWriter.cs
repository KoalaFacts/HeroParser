using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// Interface for type-specific CSV record writers.
/// </summary>
/// <typeparam name="T">The record type to write.</typeparam>
internal interface ICsvRecordWriter<in T>
{
    /// <summary>
    /// Writes the header row for this record type.
    /// </summary>
    void WriteHeader(CsvStreamWriter writer);

    /// <summary>
    /// Writes a single record as a CSV row.
    /// </summary>
    void WriteRecord(CsvStreamWriter writer, T record);
}

/// <summary>
/// Reflection-based CSV record writer that extracts values from records.
/// </summary>
/// <typeparam name="T">The record type to write.</typeparam>
public sealed class CsvRecordWriter<T> : ICsvRecordWriter<T>
{
    private static readonly ConcurrentDictionary<Type, PropertyAccessor[]> propertyCache = new();

    private readonly PropertyAccessor[] accessors;
    private readonly CsvWriterOptions writerOptions;

    // Reusable arrays to eliminate per-record allocations
    private readonly object?[] valuesBuffer;
    private readonly string?[] formatsBuffer;
    private readonly string[] headerBuffer;

    /// <summary>
    /// Creates a new reflection-based CSV record writer.
    /// </summary>
    /// <param name="options">Writer options, or null for defaults.</param>
    [RequiresUnreferencedCode("Reflection-based writing may not work with trimming. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
    [RequiresDynamicCode("Reflection-based writing requires dynamic code. Use [CsvGenerateBinder] attribute for AOT support.")]
    public CsvRecordWriter(CsvWriterOptions? options = null)
    {
        writerOptions = options ?? CsvWriterOptions.Default;
        accessors = propertyCache.GetOrAdd(typeof(T), BuildAccessors);

        // Pre-allocate buffers based on accessor count
        int count = accessors.Length;
        valuesBuffer = new object?[count];
        formatsBuffer = new string?[count];
        headerBuffer = new string[count];

        // Pre-fill format buffer (formats don't change per-record)
        for (int i = 0; i < count; i++)
        {
            formatsBuffer[i] = accessors[i].Format;
            headerBuffer[i] = accessors[i].HeaderName;
        }
    }

    /// <summary>
    /// Constructor for source-generated writers using templates.
    /// </summary>
    private CsvRecordWriter(CsvWriterOptions options, IReadOnlyList<WriterTemplate> templates)
    {
        writerOptions = options;
        accessors = InstantiateAccessors(templates);

        // Pre-allocate buffers based on accessor count
        int count = accessors.Length;
        valuesBuffer = new object?[count];
        formatsBuffer = new string?[count];
        headerBuffer = new string[count];

        // Pre-fill format buffer (formats don't change per-record)
        for (int i = 0; i < count; i++)
        {
            formatsBuffer[i] = accessors[i].Format;
            headerBuffer[i] = accessors[i].HeaderName;
        }
    }

    /// <summary>
    /// Creates a record writer from source-generated templates.
    /// </summary>
    /// <param name="options">Writer options.</param>
    /// <param name="templates">The generated templates.</param>
    /// <returns>A new record writer.</returns>
    public static CsvRecordWriter<T> CreateFromTemplates(
        CsvWriterOptions? options,
        IReadOnlyList<WriterTemplate> templates)
    {
        var resolvedOptions = options ?? CsvWriterOptions.Default;
        return new CsvRecordWriter<T>(resolvedOptions, templates);
    }

    /// <summary>
    /// Template for source-generated writers.
    /// </summary>
    /// <param name="MemberName">The property name.</param>
    /// <param name="SourceType">The property type.</param>
    /// <param name="HeaderName">The CSV column header name.</param>
    /// <param name="AttributeIndex">Optional explicit column index.</param>
    /// <param name="Format">Optional format string for the value.</param>
    /// <param name="Getter">The getter delegate for extracting the value.</param>
    public sealed record WriterTemplate(
        string MemberName,
        Type SourceType,
        string HeaderName,
        int? AttributeIndex,
        string? Format,
        Func<T, object?> Getter);

    private static PropertyAccessor[] InstantiateAccessors(IReadOnlyList<WriterTemplate> templates)
    {
        var result = new PropertyAccessor[templates.Count];
        for (int i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            result[i] = new PropertyAccessor(
                template.MemberName,
                template.HeaderName,
                template.Format,
                obj => template.Getter((T)obj));
        }
        return result;
    }

    /// <inheritdoc/>
    public void WriteHeader(CsvStreamWriter writer)
    {
        writer.WriteRow(headerBuffer);
    }

    /// <inheritdoc/>
    public void WriteRecord(CsvStreamWriter writer, T record)
    {
        WriteRecordInternal(writer, record, rowNumber: 1);
    }

    /// <summary>
    /// Writes multiple records.
    /// </summary>
    public void WriteRecords(CsvStreamWriter writer, IEnumerable<T> records, bool includeHeader = true)
    {
        int rowNumber = 0;
        int dataRowCount = 0;
        var maxRows = writerOptions.MaxRowCount;

        if (includeHeader && writerOptions.WriteHeader)
        {
            WriteHeaderRow(writer);
            rowNumber++;
        }

        foreach (var record in records)
        {
            rowNumber++;
            dataRowCount++;

            // Check MaxRowCount before writing
            if (maxRows.HasValue && dataRowCount > maxRows.Value)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Exceeded maximum row count of {maxRows.Value}");
            }

            WriteRecordInternal(writer, record, rowNumber);
        }
    }

    /// <summary>
    /// Asynchronously writes multiple records using a sync writer (for backward compatibility).
    /// </summary>
    public async ValueTask WriteRecordsAsync(
        CsvStreamWriter writer,
        IAsyncEnumerable<T> records,
        bool includeHeader = true,
        CancellationToken cancellationToken = default)
    {
        int rowNumber = 0;
        int dataRowCount = 0;
        var maxRows = writerOptions.MaxRowCount;

        if (includeHeader && writerOptions.WriteHeader)
        {
            WriteHeaderRow(writer);
            rowNumber++;
        }

        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            rowNumber++;
            dataRowCount++;

            // Check MaxRowCount before writing
            if (maxRows.HasValue && dataRowCount > maxRows.Value)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Exceeded maximum row count of {maxRows.Value}");
            }

            WriteRecordInternal(writer, record, rowNumber);
        }
    }

    /// <summary>
    /// Asynchronously writes multiple records using a true async writer.
    /// This is the preferred method for high-performance async scenarios.
    /// </summary>
    public async ValueTask WriteRecordsAsync(
        CsvAsyncStreamWriter writer,
        IAsyncEnumerable<T> records,
        bool includeHeader = true,
        CancellationToken cancellationToken = default)
    {
        int rowNumber = 0;
        int dataRowCount = 0;
        var maxRows = writerOptions.MaxRowCount;

        if (includeHeader && writerOptions.WriteHeader)
        {
            await writer.WriteRowAsync(headerBuffer, cancellationToken).ConfigureAwait(false);
            rowNumber++;
        }

        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            rowNumber++;
            dataRowCount++;

            // Check MaxRowCount before writing
            if (maxRows.HasValue && dataRowCount > maxRows.Value)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Exceeded maximum row count of {maxRows.Value}");
            }

            await WriteRecordInternalAsync(writer, record, rowNumber, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously writes multiple records from an IEnumerable using a true async writer.
    /// Avoids IAsyncEnumerable overhead for in-memory collections.
    /// </summary>
    public async ValueTask WriteRecordsAsync(
        CsvAsyncStreamWriter writer,
        IEnumerable<T> records,
        bool includeHeader = true,
        CancellationToken cancellationToken = default)
    {
        int rowNumber = 0;
        int dataRowCount = 0;
        var maxRows = writerOptions.MaxRowCount;

        if (includeHeader && writerOptions.WriteHeader)
        {
            await writer.WriteRowAsync(headerBuffer, cancellationToken).ConfigureAwait(false);
            rowNumber++;
        }

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            dataRowCount++;

            // Check MaxRowCount before writing
            if (maxRows.HasValue && dataRowCount > maxRows.Value)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Exceeded maximum row count of {maxRows.Value}");
            }

            await WriteRecordInternalAsync(writer, record, rowNumber, cancellationToken).ConfigureAwait(false);
        }
    }

    private void WriteRecordInternal(CsvStreamWriter writer, T record, int rowNumber)
    {
        if (record is null)
        {
            // Write empty row for null record
            writer.EndRow();
            return;
        }

        // Use pre-allocated buffers instead of allocating per-record
        for (int i = 0; i < accessors.Length; i++)
        {
            var accessor = accessors[i];
            try
            {
                valuesBuffer[i] = accessor.GetValue(record);
            }
            catch (Exception ex)
            {
                // Check if there's an error handler
                if (writerOptions.OnSerializeError is not null)
                {
                    var context = new CsvSerializeErrorContext
                    {
                        Row = rowNumber,
                        Column = i + 1,
                        MemberName = accessor.MemberName,
                        SourceType = typeof(T),
                        Value = null, // Value unavailable since getter failed
                        Exception = ex
                    };

                    var action = writerOptions.OnSerializeError(context);
                    switch (action)
                    {
                        case SerializeErrorAction.SkipRow:
                            return; // Don't write this row at all
                        case SerializeErrorAction.WriteNull:
                            valuesBuffer[i] = null; // Will be written as NullValue
                            continue;
                        case SerializeErrorAction.Throw:
                        default:
                            break;
                    }
                }

                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row {rowNumber}, Column {i + 1}: Failed to get value for member '{accessor.MemberName}': {ex.Message}",
                    ex);
            }
        }
        writer.WriteRowWithFormats(valuesBuffer, formatsBuffer);
    }

    private async ValueTask WriteRecordInternalAsync(CsvAsyncStreamWriter writer, T record, int rowNumber, CancellationToken cancellationToken)
    {
        if (record is null)
        {
            // Write empty row for null record
            await writer.EndRowAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // Use pre-allocated buffers instead of allocating per-record
        for (int i = 0; i < accessors.Length; i++)
        {
            var accessor = accessors[i];
            try
            {
                valuesBuffer[i] = accessor.GetValue(record);
            }
            catch (Exception ex)
            {
                // Check if there's an error handler
                if (writerOptions.OnSerializeError is not null)
                {
                    var context = new CsvSerializeErrorContext
                    {
                        Row = rowNumber,
                        Column = i + 1,
                        MemberName = accessor.MemberName,
                        SourceType = typeof(T),
                        Value = null, // Value unavailable since getter failed
                        Exception = ex
                    };

                    var action = writerOptions.OnSerializeError(context);
                    switch (action)
                    {
                        case SerializeErrorAction.SkipRow:
                            return; // Don't write this row at all
                        case SerializeErrorAction.WriteNull:
                            valuesBuffer[i] = null; // Will be written as NullValue
                            continue;
                        case SerializeErrorAction.Throw:
                        default:
                            break;
                    }
                }

                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row {rowNumber}, Column {i + 1}: Failed to get value for member '{accessor.MemberName}': {ex.Message}",
                    ex);
            }
        }
        await writer.WriteRowAsync(valuesBuffer, cancellationToken).ConfigureAwait(false);
    }

    private void WriteHeaderRow(CsvStreamWriter writer)
    {
        writer.WriteRow(headerBuffer);
    }

    private static PropertyAccessor[] BuildAccessors(Type type)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetMethod is { IsStatic: false })
            .OrderBy(p => p.MetadataToken)
            .ToList();

        var accessors = new PropertyAccessor[properties.Count];
        for (int i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            var attribute = property.GetCustomAttribute<CsvColumnAttribute>();
            var headerName = !string.IsNullOrWhiteSpace(attribute?.Name) ? attribute!.Name! : property.Name;
            var format = attribute?.Format;

            accessors[i] = new PropertyAccessor(
                property.Name,
                headerName,
                format,
                CreateGetter(property));
        }

        return accessors;
    }

    private static Func<object, object?> CreateGetter(PropertyInfo property)
    {
        // Create a compiled expression tree for ~10x faster access than MethodInfo.Invoke
        // Expression: (object instance) => (object?)((T)instance).Property
        var instanceParam = Expression.Parameter(typeof(object), "instance");

        // Cast instance from object to the declaring type
        var castInstance = Expression.Convert(instanceParam, property.DeclaringType!);

        // Access the property
        var propertyAccess = Expression.Property(castInstance, property);

        // Box the result if it's a value type, otherwise just cast to object
        Expression body = property.PropertyType.IsValueType
            ? Expression.Convert(propertyAccess, typeof(object))
            : Expression.TypeAs(propertyAccess, typeof(object));

        // Compile and return the delegate
        var lambda = Expression.Lambda<Func<object, object?>>(body, instanceParam);
        return lambda.Compile();
    }

    private sealed class PropertyAccessor(string memberName, string headerName, string? format, Func<object, object?> getter)
    {
        public string MemberName { get; } = memberName;
        public string HeaderName { get; } = headerName;
        public string? Format { get; } = format;
        private readonly Func<object, object?> getter = getter;

        public object? GetValue(object instance) => getter(instance);
    }
}

/// <summary>
/// Factory for creating record writers.
/// Resolves writers from generated code when available, falling back to runtime reflection.
/// </summary>
/// <remarks>
/// Thread-Safety: All operations are thread-safe. Each call to <see cref="GetWriter{T}"/>
/// creates a new writer instance with its own reusable buffers.
/// Property accessor metadata is cached for performance.
/// </remarks>
public static partial class CsvRecordWriterFactory
{
    private static readonly ConcurrentDictionary<Type, Func<CsvWriterOptions?, object>> generatedFactories = new();

    static CsvRecordWriterFactory()
    {
        RegisterGeneratedWriters(generatedFactories);
    }

    /// <summary>
    /// Creates a new record writer for the specified type and options.
    /// Prefers generated writers when available, falling back to reflection-based writers.
    /// </summary>
    public static CsvRecordWriter<T> GetWriter<T>(CsvWriterOptions? options = null)
    {
        options ??= CsvWriterOptions.Default;

        // Try generated writer first
        if (generatedFactories.TryGetValue(typeof(T), out var factory))
        {
            return (CsvRecordWriter<T>)factory(options);
        }

        // Fall back to reflection-based writer (property accessors are cached internally)
        return new CsvRecordWriter<T>(options);
    }

    /// <summary>
    /// Attempts to create a generated writer for the specified type.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="options">Writer options.</param>
    /// <param name="writer">The generated writer, if available.</param>
    /// <returns>True if a generated writer was found; otherwise, false.</returns>
    public static bool TryGetWriter<T>(CsvWriterOptions? options, out CsvRecordWriter<T>? writer)
    {
        if (generatedFactories.TryGetValue(typeof(T), out var factory))
        {
            options ??= CsvWriterOptions.Default;
            writer = (CsvRecordWriter<T>)factory(options);
            return true;
        }

        writer = null;
        return false;
    }

    /// <summary>
    /// Allows generated writers in referencing assemblies to register themselves at module load.
    /// </summary>
    /// <param name="type">The record type the writer handles.</param>
    /// <param name="factory">Factory for creating the writer with options.</param>
    public static void RegisterGeneratedWriter(Type type, Func<CsvWriterOptions?, object> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);

        generatedFactories[type] = factory;
    }

    /// <summary>
    /// Populated by the source generator; becomes a no-op when no generators run.
    /// </summary>
    /// <param name="factories">Cache to register writer factories into.</param>
    static partial void RegisterGeneratedWriters(ConcurrentDictionary<Type, Func<CsvWriterOptions?, object>> factories);
}
