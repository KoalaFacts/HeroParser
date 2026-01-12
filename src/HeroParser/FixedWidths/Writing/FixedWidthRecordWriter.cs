using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HeroParser.FixedWidths.Records.Binding;

namespace HeroParser.FixedWidths.Writing;

/// <summary>
/// Writes typed records to fixed-width format using column attributes.
/// </summary>
/// <typeparam name="T">The record type to write.</typeparam>
public sealed class FixedWidthRecordWriter<T>
{
    private readonly FieldDefinition[] fields;
    private readonly FixedWidthWriteOptions options;

    /// <summary>
    /// Creates a new record writer with reflection-based field discovery.
    /// </summary>
    /// <param name="options">The writer options.</param>
    public FixedWidthRecordWriter(FixedWidthWriteOptions options)
    {
        this.options = options;
        fields = BuildFieldDefinitions();

        // Calculate total record length from field definitions
        RecordLength = fields.Length > 0
            ? fields.Max(f => f.Start + f.Length)
            : 0;
    }

    /// <summary>
    /// Constructor for source-generated writers using templates.
    /// </summary>
    private FixedWidthRecordWriter(FixedWidthWriteOptions options, IReadOnlyList<WriterTemplate> templates)
    {
        this.options = options;
        fields = InstantiateFieldDefinitions(templates);

        // Calculate total record length from field definitions
        RecordLength = fields.Length > 0
            ? fields.Max(f => f.Start + f.Length)
            : 0;
    }

    /// <summary>
    /// Creates a record writer from source-generated templates.
    /// </summary>
    /// <param name="options">Writer options.</param>
    /// <param name="templates">The generated templates.</param>
    /// <returns>A new record writer.</returns>
    public static FixedWidthRecordWriter<T> CreateFromTemplates(
        FixedWidthWriteOptions options,
        IReadOnlyList<WriterTemplate> templates)
    {
        return new FixedWidthRecordWriter<T>(options, templates);
    }

    /// <summary>
    /// Template for source-generated writers.
    /// </summary>
    /// <param name="MemberName">The property name.</param>
    /// <param name="SourceType">The property type.</param>
    /// <param name="Start">The 0-based start position in the record.</param>
    /// <param name="Length">The field width.</param>
    /// <param name="Alignment">The field alignment.</param>
    /// <param name="PadChar">The padding character.</param>
    /// <param name="Format">Optional format string for the value.</param>
    /// <param name="Getter">The getter delegate for extracting the value.</param>
    public sealed record WriterTemplate(
        string MemberName,
        Type SourceType,
        int Start,
        int Length,
        FieldAlignment Alignment,
        char PadChar,
        string? Format,
        Func<T, object?> Getter);

    private static FieldDefinition[] InstantiateFieldDefinitions(IReadOnlyList<WriterTemplate> templates)
    {
        var layouts = new FixedWidthFieldLayout[templates.Count];
        for (int i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            layouts[i] = new FixedWidthFieldLayout(template.MemberName, template.Start, template.Length);
        }
        FixedWidthFieldLayoutValidator.Validate(layouts);

        var result = new FieldDefinition[templates.Count];
        for (int i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            result[i] = new FieldDefinition
            {
                Name = template.MemberName,
                PropertyType = template.SourceType,
                Start = template.Start,
                Length = template.Length,
                Alignment = template.Alignment,
                PadChar = template.PadChar == '\0' ? ' ' : template.PadChar,
                Format = template.Format,
                Getter = template.Getter
            };
        }
        return result;
    }

    /// <summary>
    /// Gets the total record length calculated from field definitions.
    /// </summary>
    public int RecordLength { get; }

    /// <summary>
    /// Writes records to the stream writer.
    /// </summary>
    public void WriteRecords(FixedWidthStreamWriter writer, IEnumerable<T> records)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(records);

        int rowCount = 0;
        foreach (var record in records)
        {
            if (record is null) continue;

            if (options.MaxRowCount.HasValue && rowCount >= options.MaxRowCount.Value)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.TooManyRowsWritten,
                    $"Maximum row count of {options.MaxRowCount.Value} exceeded");
            }

            if (WriteRecord(writer, record, rowCount + 1))
            {
                writer.EndRow();
            }
            rowCount++;
        }
    }

    /// <summary>
    /// Writes records asynchronously.
    /// </summary>
    public async ValueTask WriteRecordsAsync(
        FixedWidthStreamWriter writer,
        IAsyncEnumerable<T> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(records);

        int rowCount = 0;
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (record is null) continue;

            if (options.MaxRowCount.HasValue && rowCount >= options.MaxRowCount.Value)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.TooManyRowsWritten,
                    $"Maximum row count of {options.MaxRowCount.Value} exceeded");
            }

            if (WriteRecord(writer, record, rowCount + 1))
            {
                writer.EndRow();
            }
            rowCount++;
        }
    }

    /// <summary>
    /// Writes records from IEnumerable asynchronously (just writes, I/O is sync in this case).
    /// </summary>
    public ValueTask WriteRecordsAsync(
        FixedWidthStreamWriter writer,
        IEnumerable<T> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(records);

        int rowCount = 0;
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record is null) continue;

            if (options.MaxRowCount.HasValue && rowCount >= options.MaxRowCount.Value)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.TooManyRowsWritten,
                    $"Maximum row count of {options.MaxRowCount.Value} exceeded");
            }

            if (WriteRecord(writer, record, rowCount + 1))
            {
                writer.EndRow();
            }
            rowCount++;
        }

        return ValueTask.CompletedTask;
    }

    private bool WriteRecord(FixedWidthStreamWriter writer, T record, int rowNumber)
    {
        // Pre-allocate buffer for the entire record
        Span<char> recordBuffer = RecordLength <= 256
            ? stackalloc char[RecordLength]
            : new char[RecordLength];

        // Fill with default pad char
        recordBuffer.Fill(options.DefaultPadChar);

        // Write each field into the buffer
        for (int i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            var value = field.GetValue(record);

            try
            {
                WriteFieldToBuffer(recordBuffer, field, value);
            }
            catch (Exception ex) when (options.OnSerializeError is not null)
            {
                var context = new FixedWidthSerializeErrorContext
                {
                    Row = rowNumber,
                    Column = i,
                    MemberName = field.Name,
                    SourceType = field.PropertyType,
                    Value = value,
                    Exception = ex
                };

                var action = options.OnSerializeError(context);
                if (action == FixedWidthSerializeErrorAction.Throw)
                    throw;
                if (action == FixedWidthSerializeErrorAction.SkipRow)
                    return false; // Don't write this row
                // FixedWidthSerializeErrorAction.WriteEmpty: Already filled with pad chars
            }
        }

        // Write the complete record buffer
        writer.WriteField(recordBuffer, RecordLength);
        return true;
    }

    private void WriteFieldToBuffer(Span<char> recordBuffer, FieldDefinition field, object? value)
    {
        var fieldSpan = recordBuffer.Slice(field.Start, field.Length);

        // Fill with field's pad char (may differ from default)
        fieldSpan.Fill(field.PadChar);

        if (value is null)
        {
            var nullText = options.NullValue.AsSpan();
            WriteAligned(fieldSpan, nullText, field.Alignment, field.PadChar);
            return;
        }

        // Format the value
        Span<char> formatted = stackalloc char[256];

        if (!TryFormatValue(value, formatted, field.Format, out int charsWritten))
        {
            // Fallback to string allocation
            var str = FormatValueToString(value, field.Format);
            WriteAligned(fieldSpan, str.AsSpan(), field.Alignment, field.PadChar);
            return;
        }

        WriteAligned(fieldSpan, formatted[..charsWritten], field.Alignment, field.PadChar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteAligned(Span<char> fieldSpan, ReadOnlySpan<char> value, FieldAlignment alignment, char padChar)
    {
        int width = fieldSpan.Length;

        if (value.Length >= width)
        {
            // Truncate based on overflow behavior
            if (options.OverflowBehavior == OverflowBehavior.Throw)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.FieldOverflow,
                    $"Field value length {value.Length} exceeds width {width}");
            }

            // Truncate from appropriate end based on alignment
            if (alignment == FieldAlignment.Right)
            {
                value[(value.Length - width)..].CopyTo(fieldSpan);
            }
            else
            {
                value[..width].CopyTo(fieldSpan);
            }
            return;
        }

        // Pad based on alignment
        int paddingNeeded = width - value.Length;

        switch (alignment)
        {
            case FieldAlignment.Right:
                fieldSpan[..paddingNeeded].Fill(padChar);
                value.CopyTo(fieldSpan[paddingNeeded..]);
                break;

            case FieldAlignment.Center:
                int leftPad = paddingNeeded / 2;
                fieldSpan[..leftPad].Fill(padChar);
                value.CopyTo(fieldSpan[leftPad..]);
                fieldSpan[(leftPad + value.Length)..].Fill(padChar);
                break;

            case FieldAlignment.Left:
            case FieldAlignment.None:
            default:
                value.CopyTo(fieldSpan);
                fieldSpan[value.Length..].Fill(padChar);
                break;
        }
    }

    private bool TryFormatValue(object value, Span<char> destination, string? format, out int charsWritten)
    {
        var culture = options.Culture;

        switch (value)
        {
            case string s:
                if (s.Length <= destination.Length)
                {
                    s.AsSpan().CopyTo(destination);
                    charsWritten = s.Length;
                    return true;
                }
                charsWritten = 0;
                return false;

            case int i:
                return i.TryFormat(destination, out charsWritten, format ?? options.NumberFormat, culture);

            case long l:
                return l.TryFormat(destination, out charsWritten, format ?? options.NumberFormat, culture);

            case double d:
                return d.TryFormat(destination, out charsWritten, format ?? options.NumberFormat, culture);

            case decimal dec:
                return dec.TryFormat(destination, out charsWritten, format ?? options.NumberFormat, culture);

            case bool b:
                var boolStr = b ? "True" : "False";
                if (boolStr.Length <= destination.Length)
                {
                    boolStr.AsSpan().CopyTo(destination);
                    charsWritten = boolStr.Length;
                    return true;
                }
                charsWritten = 0;
                return false;

            case DateTime dt:
                return dt.TryFormat(destination, out charsWritten, format ?? options.DateTimeFormat, culture);

            case DateTimeOffset dto:
                return dto.TryFormat(destination, out charsWritten, format ?? options.DateTimeFormat, culture);

#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                return dateOnly.TryFormat(destination, out charsWritten, format ?? options.DateOnlyFormat, culture);

            case TimeOnly timeOnly:
                return timeOnly.TryFormat(destination, out charsWritten, format ?? options.TimeOnlyFormat, culture);
#endif

            case float f:
                return f.TryFormat(destination, out charsWritten, format ?? options.NumberFormat, culture);

            case byte by:
                return by.TryFormat(destination, out charsWritten, format ?? options.NumberFormat, culture);

            case short sh:
                return sh.TryFormat(destination, out charsWritten, format ?? options.NumberFormat, culture);

            case uint ui:
                return ui.TryFormat(destination, out charsWritten, format ?? options.NumberFormat, culture);

            case ulong ul:
                return ul.TryFormat(destination, out charsWritten, format ?? options.NumberFormat, culture);

            case Guid g:
                return g.TryFormat(destination, out charsWritten, format);

            case ISpanFormattable spanFormattable:
                return spanFormattable.TryFormat(destination, out charsWritten, format, culture);

            default:
                charsWritten = 0;
                return false;
        }
    }

    private string FormatValueToString(object value, string? format)
    {
        var culture = options.Culture;

        if (value is IFormattable formattable)
        {
            return formattable.ToString(format, culture);
        }

        return value.ToString() ?? string.Empty;
    }

    private static FieldDefinition[] BuildFieldDefinitions()
    {
        var type = typeof(T);
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Property: p, Attr: p.GetCustomAttribute<FixedWidthColumnAttribute>()))
            .Where(x => x.Attr is not null)
            .OrderBy(x => x.Attr!.Start)
            .ToArray();

        var layouts = new FixedWidthFieldLayout[props.Length];
        for (int i = 0; i < props.Length; i++)
        {
            var (prop, attr) = props[i];
            layouts[i] = new FixedWidthFieldLayout(prop.Name, attr!.Start, attr.Length);
        }
        FixedWidthFieldLayoutValidator.Validate(layouts);

        var fields = new FieldDefinition[props.Length];
        for (int i = 0; i < props.Length; i++)
        {
            var (prop, attr) = props[i];
            fields[i] = new FieldDefinition
            {
                Name = prop.Name,
                PropertyType = prop.PropertyType,
                Start = attr!.Start,
                Length = attr.Length,
                Alignment = attr.Alignment,
                PadChar = attr.PadChar == '\0' ? ' ' : attr.PadChar,
                Format = attr.Format,
                Getter = BuildGetter(prop)
            };
        }

        return fields;
    }

    private static Func<T, object?> BuildGetter(PropertyInfo prop)
    {
        // Use compiled expression for better performance
        var param = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
        var access = System.Linq.Expressions.Expression.Property(param, prop);
        var convert = System.Linq.Expressions.Expression.Convert(access, typeof(object));
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<T, object?>>(convert, param);
        return lambda.Compile();
    }

    private readonly struct FieldDefinition
    {
        public required string Name { get; init; }
        public required Type PropertyType { get; init; }
        public required int Start { get; init; }
        public required int Length { get; init; }
        public required FieldAlignment Alignment { get; init; }
        public required char PadChar { get; init; }
        public required string? Format { get; init; }
        public required Func<T, object?> Getter { get; init; }

        public object? GetValue(T record) => Getter(record);
    }
}

/// <summary>
/// Factory for creating cached FixedWidthRecordWriter instances.
/// </summary>
public static partial class FixedWidthRecordWriterFactory
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<FixedWidthWriteOptions, object>> generatedFactories = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, FixedWidthWriteOptions), object> reflectionCache = new();

    static FixedWidthRecordWriterFactory()
    {
        RegisterGeneratedWriters(generatedFactories);
    }

    /// <summary>
    /// Creates a new record writer for the specified type and options.
    /// Prefers generated writers when available, falling back to reflection-based writers.
    /// </summary>
    public static FixedWidthRecordWriter<T> GetWriter<T>(FixedWidthWriteOptions options)
    {
        // Try generated writer first (not cached - each call creates new instance with options)
        if (generatedFactories.TryGetValue(typeof(T), out var factory))
        {
            return (FixedWidthRecordWriter<T>)factory(options);
        }

        // Fall back to reflection-based writer (cached)
        var key = (typeof(T), options);
        return (FixedWidthRecordWriter<T>)reflectionCache.GetOrAdd(key, _ => new FixedWidthRecordWriter<T>(options));
    }

    /// <summary>
    /// Allows generated writers in referencing assemblies to register themselves at module load.
    /// </summary>
    /// <param name="type">The record type the writer handles.</param>
    /// <param name="factory">Factory for creating the writer with options.</param>
    public static void RegisterGeneratedWriter(Type type, Func<FixedWidthWriteOptions, object> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);

        generatedFactories[type] = factory;
    }

    /// <summary>
    /// Populated by the source generator; becomes a no-op when no generators run.
    /// </summary>
    /// <param name="factories">Cache to register writer factories into.</param>
    static partial void RegisterGeneratedWriters(System.Collections.Concurrent.ConcurrentDictionary<Type, Func<FixedWidthWriteOptions, object>> factories);
}

