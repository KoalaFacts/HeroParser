using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;
using HeroParser.Validation;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;

namespace HeroParser.Excels.Writing;

/// <summary>
/// Writes typed records to an Excel worksheet using reflection or source-generated accessors.
/// </summary>
/// <typeparam name="T">The record type to write.</typeparam>
public sealed class ExcelRecordWriter<T>
{
    private static readonly ConcurrentDictionary<Type, PropertyAccessor[]> propertyCache = new();

    /// <summary>
    /// Delegate for source-generated direct record writing that avoids boxing value-type properties.
    /// </summary>
    public delegate void DirectRecordWriterDelegate(
        XlsxWriter.SheetWriter sheetWriter,
        T record,
        int rowNumber,
        ExcelWriteOptions options);

    private readonly PropertyAccessor[] accessors;
    private readonly ExcelWriteOptions writerOptions;
    private readonly DirectRecordWriterDelegate? directRecordWriter;

    // Pre-allocated reusable buffers to avoid per-record allocations
    private readonly object?[] valuesBuffer;
    private readonly string[] headerBuffer;

    /// <summary>
    /// Creates a new reflection-based Excel record writer for type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="options">Writer options, or <see langword="null"/> to use defaults.</param>
    [RequiresUnreferencedCode("Reflection-based writing may not work with trimming. Use [GenerateBinder] attribute for AOT/trimming support.")]
    [RequiresDynamicCode("Reflection-based writing requires dynamic code. Use [GenerateBinder] attribute for AOT support.")]
    public ExcelRecordWriter(ExcelWriteOptions? options = null)
    {
        writerOptions = options ?? ExcelWriteOptions.Default;
        accessors = propertyCache.GetOrAdd(typeof(T), BuildAccessors);

        int count = accessors.Length;
        valuesBuffer = new object?[count];
        headerBuffer = new string[count];

        for (int i = 0; i < count; i++)
            headerBuffer[i] = accessors[i].HeaderName;
    }

    /// <summary>
    /// Constructor for source-generated writers using pre-built templates.
    /// </summary>
    private ExcelRecordWriter(ExcelWriteOptions options, IReadOnlyList<WriterTemplate> templates,
        DirectRecordWriterDelegate? directWriter = null)
    {
        writerOptions = options;
        accessors = InstantiateAccessors(templates);
        directRecordWriter = directWriter;

        int count = accessors.Length;
        valuesBuffer = new object?[count];
        headerBuffer = new string[count];

        for (int i = 0; i < count; i++)
            headerBuffer[i] = accessors[i].HeaderName;
    }

    /// <summary>
    /// Template used by source generators to provide property metadata without reflection.
    /// </summary>
    /// <param name="MemberName">The property name on the record type.</param>
    /// <param name="SourceType">The declared type of the property.</param>
    /// <param name="HeaderName">The Excel column header name.</param>
    /// <param name="Format">Optional format string applied when writing the value.</param>
    /// <param name="Getter">Delegate that extracts the property value from a record instance.</param>
    /// <param name="Validation">Optional write-side validation rules derived from <see cref="ValidateAttribute"/>.</param>
    public sealed record WriterTemplate(
        string MemberName,
        Type SourceType,
        string HeaderName,
        string? Format,
        Func<T, object?> Getter,
        WritePropertyValidation? Validation = null);

    /// <summary>
    /// Creates a record writer from source-generated templates.
    /// </summary>
    /// <param name="options">Writer options, or <see langword="null"/> to use defaults.</param>
    /// <param name="templates">The source-generated templates describing each property.</param>
    /// <returns>A new <see cref="ExcelRecordWriter{T}"/> backed by the provided templates.</returns>
    public static ExcelRecordWriter<T> CreateFromTemplates(
        ExcelWriteOptions? options,
        IReadOnlyList<WriterTemplate> templates)
    {
        return new ExcelRecordWriter<T>(options ?? ExcelWriteOptions.Default, templates);
    }

    /// <summary>
    /// Creates a record writer from source-generated templates with a direct record writer delegate
    /// that avoids boxing value-type properties.
    /// </summary>
    /// <param name="options">Writer options, or <see langword="null"/> to use defaults.</param>
    /// <param name="templates">The source-generated templates describing each property.</param>
    /// <param name="directWriter">A delegate that writes a record directly to a sheet writer without boxing.</param>
    /// <returns>A new <see cref="ExcelRecordWriter{T}"/> backed by the provided templates and direct writer.</returns>
    public static ExcelRecordWriter<T> CreateFromTemplates(
        ExcelWriteOptions? options,
        IReadOnlyList<WriterTemplate> templates,
        DirectRecordWriterDelegate directWriter)
    {
        return new ExcelRecordWriter<T>(options ?? ExcelWriteOptions.Default, templates, directWriter);
    }

    /// <summary>
    /// Writes a sequence of records to the given sheet writer.
    /// </summary>
    /// <param name="sheetWriter">The sheet writer to write into.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">
    /// Optional options override. When <see langword="null"/>, the options passed to the constructor are used.
    /// </param>
    /// <param name="sheetName">The name of the worksheet, used in progress reports and error contexts.</param>
    /// <param name="xlsxWriter">The optional spreadsheet package writer to register custom styles.</param>
    /// <param name="headerStyle">The optional style for the header row.</param>
    /// <param name="columnStyles">The optional dictionary mapping properties to column styles.</param>
    /// <param name="mergedCellRanges">The optional list of explicit cell ranges to merge.</param>
    /// <param name="autoMergeColumns">The optional list of property names whose duplicate contiguous values should be auto-merged.</param>
    internal void WriteRecords(
        XlsxWriter.SheetWriter sheetWriter,
        IEnumerable<T> records,
        ExcelWriteOptions? options = null,
        string sheetName = "Sheet1",
        XlsxWriter? xlsxWriter = null,
        ExcelStyle? headerStyle = null,
        Dictionary<string, ExcelStyle>? columnStyles = null,
        List<ExcelMergeRange>? mergedCellRanges = null,
        List<string>? autoMergeColumns = null)
    {
        var effectiveOptions = options ?? writerOptions;
        effectiveOptions.Validate();
        var progress = effectiveOptions.WriteProgress;
        int intervalRows = effectiveOptions.WriteProgressIntervalRows > 0
            ? effectiveOptions.WriteProgressIntervalRows
            : 1000;

        int? headerStyleIndex = null;
        int?[]? columnStyleIndices = null;

        if (xlsxWriter is not null)
        {
            if (headerStyle is not null)
                headerStyleIndex = xlsxWriter.RegisterStyle(headerStyle);

            if (columnStyles is not null && columnStyles.Count > 0)
            {
                columnStyleIndices = new int?[accessors.Length];
                for (int i = 0; i < accessors.Length; i++)
                {
                    if (columnStyles.TryGetValue(accessors[i].MemberName, out var style))
                        columnStyleIndices[i] = xlsxWriter.RegisterStyle(style);
                }
            }
        }

        // Apply explicit merged cell ranges
        if (mergedCellRanges is not null && mergedCellRanges.Count > 0)
        {
            foreach (var merge in mergedCellRanges)
            {
                if (merge.IsCoordinate)
                    sheetWriter.MergeCells(merge.StartColumn, merge.StartRow, merge.EndColumn, merge.EndRow);
                else if (merge.RangeString is not null)
                    sheetWriter.MergeCells(merge.RangeString);
            }
        }

        // Initialize trackers for auto-merging duplicate contiguous values
        MergeTracker[]? trackers = null;
        if (autoMergeColumns is not null && autoMergeColumns.Count > 0)
        {
            trackers = new MergeTracker[accessors.Length];
            int startRow = effectiveOptions.WriteHeader ? 2 : 1;
            for (int i = 0; i < accessors.Length; i++)
            {
                if (autoMergeColumns.Contains(accessors[i].MemberName))
                {
                    trackers[i] = new MergeTracker
                    {
                        Enabled = true,
                        StartRow = startRow,
                        HasValue = false
                    };
                }
            }
        }

        // Row 1 is header (if enabled); data starts at row 2 when header is written, else row 1.
        int rowNumber = 0;

        if (effectiveOptions.WriteHeader)
        {
            sheetWriter.StartRow(1);
            for (int col = 0; col < headerBuffer.Length; col++)
                sheetWriter.WriteCellString(col + 1, headerBuffer[col], headerStyleIndex);
            sheetWriter.EndRow();
            rowNumber = 1;
        }

        int dataRowCount = 0;
        foreach (var record in records)
        {
            if (effectiveOptions.MaxRowCount.HasValue && dataRowCount >= effectiveOptions.MaxRowCount.Value)
                throw new ExcelException(
                    $"Maximum row count of {effectiveOptions.MaxRowCount.Value} exceeded.");

            if (effectiveOptions.MaxOutputSize.HasValue && sheetWriter.BytesWritten > effectiveOptions.MaxOutputSize.Value)
                throw new ExcelException(
                    $"Maximum output size of {effectiveOptions.MaxOutputSize.Value} bytes exceeded.");

            rowNumber++;
            bool written = WriteRecordInternal(sheetWriter, record, rowNumber, effectiveOptions, sheetName, dataRowCount + 1, columnStyleIndices, trackers);
            if (written)
                dataRowCount++;
            else
                rowNumber--; // row was skipped — undo the increment so the next row gets the same number

            if (progress is not null && dataRowCount > 0 && dataRowCount % intervalRows == 0)
                progress.Report(new ExcelWriteProgress { RowsWritten = dataRowCount, SheetName = sheetName });
        }

        // Close any pending auto-merges at the end of the sheet
        if (trackers is not null && rowNumber > 0)
        {
            for (int i = 0; i < trackers.Length; i++)
            {
                if (trackers[i].Enabled && trackers[i].HasValue)
                {
                    if (rowNumber > trackers[i].StartRow)
                    {
                        sheetWriter.MergeCells(i + 1, trackers[i].StartRow, i + 1, rowNumber);
                    }
                }
            }
        }

        // Report final progress (skip if already reported at interval boundary)
        if (progress is not null && (dataRowCount == 0 || dataRowCount % intervalRows != 0))
            progress.Report(new ExcelWriteProgress { RowsWritten = dataRowCount, SheetName = sheetName });
    }

    // Returns true if the row was written, false if it was skipped via SkipRow error action.
    private bool WriteRecordInternal(
        XlsxWriter.SheetWriter sheetWriter,
        T record,
        int rowNumber,
        ExcelWriteOptions options,
        string sheetName,
        int dataRow,
        int?[]? columnStyleIndices,
        MergeTracker[]? trackers)
    {
        if (record is null)
        {
            // Write a row of empty cells for null records
            sheetWriter.StartRow(rowNumber);
            for (int i = 0; i < accessors.Length; i++)
            {
                int? styleIdx = columnStyleIndices?[i];
                sheetWriter.WriteCellEmpty(i + 1, styleIdx);
            }
            sheetWriter.EndRow();
            return true;
        }

        // Validate before writing when in Strict mode (both generated and reflection paths)
        if (options.ValidationMode == ValidationMode.Strict)
        {
            List<ValidationError>? validationErrors = null;
            for (int i = 0; i < accessors.Length; i++)
            {
                var accessor = accessors[i];
                if (accessor.Validation is { HasAnyRule: true } rules)
                {
                    validationErrors ??= [];
                    WriteValidationRunner.Validate(accessor.GetValue(record), accessor.MemberName, rowNumber, i, rules, validationErrors);
                }
            }

            if (validationErrors is { Count: > 0 })
                throw new ValidationException(validationErrors);
        }

        // Source-generated direct writer: avoids boxing value-type properties.
        // Bypassed if we are tracking/auto-merging duplicate contiguous values in columns.
        if (directRecordWriter is not null && trackers is null)
        {
            directRecordWriter(sheetWriter, record, rowNumber, options);
            return true;
        }

        // Reflection path: extract values into buffer (boxes value types), with optional error handling
        var errorHandler = options.OnSerializeError;
        if (errorHandler is null)
        {
            for (int i = 0; i < accessors.Length; i++)
                valuesBuffer[i] = accessors[i].GetValue(record);
        }
        else
        {
            for (int i = 0; i < accessors.Length; i++)
            {
                try
                {
                    valuesBuffer[i] = accessors[i].GetValue(record);
                }
                catch (Exception ex)
                {
                    var ctx = new ExcelSerializeErrorContext
                    {
                        Row = dataRow,
                        Column = i + 1,
                        MemberName = accessors[i].MemberName,
                        SourceType = accessors[i].SourceType,
                        Value = null,
                        Exception = ex,
                        SheetName = sheetName,
                    };
                    var action = errorHandler(ctx);
                    switch (action)
                    {
                        case ExcelSerializeErrorAction.SkipRow:
                            return false;
                        case ExcelSerializeErrorAction.WriteEmpty:
                            valuesBuffer[i] = null;
                            break;
                        case ExcelSerializeErrorAction.Throw:
                        default:
                            throw new ExcelException(
                                $"Serialization error at row {dataRow}, column {i} ({accessors[i].MemberName}).", ex);
                    }
                }
            }
        }

        sheetWriter.StartRow(rowNumber);
        for (int i = 0; i < accessors.Length; i++)
        {
            int? styleIdx = columnStyleIndices?[i];
            object? val = valuesBuffer[i];

            if (trackers is not null && trackers[i].Enabled)
            {
                ref var tracker = ref trackers[i];
                if (!tracker.HasValue)
                {
                    tracker.LastValue = val;
                    tracker.HasValue = true;
                    WriteCellValue(sheetWriter, i + 1, val, accessors[i].Format, options, styleIdx);
                }
                else if (object.Equals(val, tracker.LastValue))
                {
                    // Contiguous duplicate value: write empty cell
                    sheetWriter.WriteCellEmpty(i + 1, styleIdx);
                }
                else
                {
                    // Value changed: merge the completed contiguous block
                    int endRow = rowNumber - 1;
                    if (endRow > tracker.StartRow)
                    {
                        sheetWriter.MergeCells(i + 1, tracker.StartRow, i + 1, endRow);
                    }
                    tracker.StartRow = rowNumber;
                    tracker.LastValue = val;
                    WriteCellValue(sheetWriter, i + 1, val, accessors[i].Format, options, styleIdx);
                }
            }
            else
            {
                WriteCellValue(sheetWriter, i + 1, val, accessors[i].Format, options, styleIdx);
            }
        }
        sheetWriter.EndRow();
        return true;
    }

    private static void WriteCellValue(
        XlsxWriter.SheetWriter sheetWriter,
        int columnIndex,
        object? value,
        string? format,
        ExcelWriteOptions options,
        int? styleIndex)
    {
        if (value is null)
        {
            sheetWriter.WriteCellEmpty(columnIndex, styleIndex);
            return;
        }

        var culture = options.Culture ?? CultureInfo.InvariantCulture;

        switch (value)
        {
            case string s:
                if (s.Length == 0 || s == options.NullValue)
                    sheetWriter.WriteCellEmpty(columnIndex, styleIndex);
                else
                    sheetWriter.WriteCellString(columnIndex, s, styleIndex);
                return;

            case bool b:
                if (format is not null)
                    sheetWriter.WriteCellString(columnIndex, b.ToString(culture), styleIndex);
                else
                    sheetWriter.WriteCellBoolean(columnIndex, b, styleIndex);
                return;

            case DateTime dt:
                if (format is not null || options.DateTimeFormat is not null)
                    sheetWriter.WriteCellString(columnIndex, dt.ToString(format ?? options.DateTimeFormat, culture), styleIndex);
                else
                    sheetWriter.WriteCellDate(columnIndex, dt, styleIndex);
                return;

            case DateTimeOffset dto:
                // Default to ISO 8601 round-trip format ("O") to preserve UTC offset.
                // When an explicit format is provided, use that instead.
                sheetWriter.WriteCellString(columnIndex, dto.ToString(format ?? options.DateTimeFormat ?? "O", culture), styleIndex);
                return;

            case DateOnly d:
                {
                    var dtValue = d.ToDateTime(TimeOnly.MinValue);
                    if (format is not null || options.DateOnlyFormat is not null)
                        sheetWriter.WriteCellString(columnIndex, d.ToString(format ?? options.DateOnlyFormat, culture), styleIndex);
                    else
                        sheetWriter.WriteCellDate(columnIndex, dtValue, styleIndex);
                }
                return;

            case TimeOnly t:
                sheetWriter.WriteCellString(columnIndex, t.ToString(format ?? options.TimeOnlyFormat ?? "HH:mm:ss", culture), styleIndex);
                return;

            case Guid g:
                sheetWriter.WriteCellString(columnIndex, g.ToString(), styleIndex);
                return;

            case int i:
                WriteNumericCell(sheetWriter, columnIndex, i, format, options, culture, styleIndex);
                return;
            case long l:
                WriteNumericCell(sheetWriter, columnIndex, l, format, options, culture, styleIndex);
                return;
            case short s16:
                WriteNumericCell(sheetWriter, columnIndex, s16, format, options, culture, styleIndex);
                return;
            case byte b8:
                WriteNumericCell(sheetWriter, columnIndex, b8, format, options, culture, styleIndex);
                return;
            case uint u:
                WriteNumericCell(sheetWriter, columnIndex, u, format, options, culture, styleIndex);
                return;
            case ulong ul:
                WriteNumericCell(sheetWriter, columnIndex, ul, format, options, culture, styleIndex);
                return;
            case ushort u16:
                WriteNumericCell(sheetWriter, columnIndex, u16, format, options, culture, styleIndex);
                return;
            case sbyte sb8:
                WriteNumericCell(sheetWriter, columnIndex, sb8, format, options, culture, styleIndex);
                return;
            case double d:
                WriteNumericCell(sheetWriter, columnIndex, d, format, options, culture, styleIndex);
                return;
            case float f:
                WriteNumericCell(sheetWriter, columnIndex, f, format, options, culture, styleIndex);
                return;

            case decimal dec:
                // Always write decimal as string to preserve full precision (double cast loses >15 significant digits)
                sheetWriter.WriteCellString(columnIndex, dec.ToString(format ?? options.NumberFormat, culture), styleIndex);
                return;

            default:
                // Fallback: use IFormattable if format specified, otherwise ToString
                if (format is not null && value is IFormattable formattable)
                    sheetWriter.WriteCellString(columnIndex, formattable.ToString(format, culture), styleIndex);
                else
                {
                    var str = value.ToString();
                    if (str is null || str.Length == 0)
                        sheetWriter.WriteCellEmpty(columnIndex, styleIndex);
                    else
                        sheetWriter.WriteCellString(columnIndex, str, styleIndex);
                }
                return;
        }
    }

    private static void WriteNumericCell<TNum>(
        XlsxWriter.SheetWriter sheetWriter,
        int columnIndex,
        TNum value,
        string? format,
        ExcelWriteOptions options,
        CultureInfo culture,
        int? styleIndex)
        where TNum : INumber<TNum>
    {
        if (format is not null || options.NumberFormat is not null)
            sheetWriter.WriteCellString(columnIndex, value.ToString(format ?? options.NumberFormat, culture), styleIndex);
        else
            sheetWriter.WriteCellNumber(columnIndex, double.CreateChecked(value), styleIndex);
    }

    private static PropertyAccessor[] InstantiateAccessors(IReadOnlyList<WriterTemplate> templates)
    {
        var result = new PropertyAccessor[templates.Count];
        for (int i = 0; i < templates.Count; i++)
        {
            var t = templates[i];
            result[i] = new PropertyAccessor(
                t.MemberName,
                t.HeaderName,
                t.Format,
                obj => t.Getter((T)obj),
                t.SourceType,
                t.Validation);
        }
        return result;
    }

    [RequiresUnreferencedCode("Uses reflection over T.GetProperties; only reached via the [RequiresUnreferencedCode] constructor.")]
    [RequiresDynamicCode("Compiles property getters with Expression.Compile; only reached via the [RequiresDynamicCode] constructor.")]
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
            var tabularMap = property.GetCustomAttribute<TabularMapAttribute>();
            var parseAttr = property.GetCustomAttribute<ParseAttribute>();
            var formatAttr = property.GetCustomAttribute<FormatAttribute>();
            var validateAttr = property.GetCustomAttribute<ValidateAttribute>();

            var headerName = !string.IsNullOrWhiteSpace(tabularMap?.Name) ? tabularMap.Name : property.Name;
            var format = formatAttr?.WriteFormat ?? parseAttr?.Format;

            WritePropertyValidation? validation = null;
            if (validateAttr is not null)
            {
                validation = new WritePropertyValidation(
                    validateAttr.NotNull,
                    validateAttr.NotEmpty,
                    validateAttr.MaxLength >= 0 ? validateAttr.MaxLength : null,
                    validateAttr.MinLength >= 0 ? validateAttr.MinLength : null,
                    !double.IsNaN(validateAttr.RangeMin) ? validateAttr.RangeMin : null,
                    !double.IsNaN(validateAttr.RangeMax) ? validateAttr.RangeMax : null,
                    validateAttr.Pattern,
                    validateAttr.PatternTimeoutMs);
            }

            accessors[i] = new PropertyAccessor(
                property.Name,
                headerName,
                format,
                CreateGetter(property),
                property.PropertyType,
                validation);
        }

        return accessors;
    }

    [RequiresDynamicCode("Uses Expression.Compile to emit a property getter at runtime.")]
    private static Func<object, object?> CreateGetter(PropertyInfo property)
    {
        // Compiled expression tree for ~10x faster access than MethodInfo.Invoke
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var castInstance = Expression.Convert(instanceParam, property.DeclaringType!);
        var propertyAccess = Expression.Property(castInstance, property);

        Expression body = property.PropertyType.IsValueType
            ? Expression.Convert(propertyAccess, typeof(object))
            : Expression.TypeAs(propertyAccess, typeof(object));

        return Expression.Lambda<Func<object, object?>>(body, instanceParam).Compile();
    }

    private sealed class PropertyAccessor(
        string memberName,
        string headerName,
        string? format,
        Func<object, object?> getter,
        Type sourceType,
        WritePropertyValidation? validation = null)
    {
        public string MemberName { get; } = memberName;
        public string HeaderName { get; } = headerName;
        public string? Format { get; } = format;
        public Type SourceType { get; } = sourceType;
        public WritePropertyValidation? Validation { get; } = validation;
        private readonly Func<object, object?> getter = getter;

        public object? GetValue(object instance) => getter(instance);
    }

    private struct MergeTracker
    {
        public bool Enabled;
        public object? LastValue;
        public int StartRow;
        public bool HasValue;
    }
}
