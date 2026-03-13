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
    private readonly CsvWriteOptions writerOptions;
    private readonly bool needsEmptyColumnScan;

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
    public CsvRecordWriter(CsvWriteOptions? options = null)
    {
        writerOptions = options ?? CsvWriteOptions.Default;
        accessors = propertyCache.GetOrAdd(typeof(T), BuildAccessors);
        needsEmptyColumnScan = ComputeNeedsEmptyColumnScan();

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
    private CsvRecordWriter(CsvWriteOptions options, IReadOnlyList<WriterTemplate> templates)
    {
        writerOptions = options;
        accessors = InstantiateAccessors(templates);
        needsEmptyColumnScan = ComputeNeedsEmptyColumnScan();

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
        CsvWriteOptions? options,
        IReadOnlyList<WriterTemplate> templates)
    {
        var resolvedOptions = options ?? CsvWriteOptions.Default;
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
    /// <param name="ExcludeFromWriteIfAllEmpty">Whether to exclude this column from output when all values are empty.</param>
    public sealed record WriterTemplate(
        string MemberName,
        Type SourceType,
        string HeaderName,
        int? AttributeIndex,
        string? Format,
        Func<T, object?> Getter,
        bool ExcludeFromWriteIfAllEmpty = false);

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
                template.ExcludeFromWriteIfAllEmpty,
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
        if (needsEmptyColumnScan)
        {
            var materialized = MaterializeRecords(records, writerOptions.MaxRowCount);
            WriteRecordsFiltered(writer, materialized, includeHeader);
            return;
        }

        int rowNumber = 0;
        int dataRowCount = 0;
        var maxRows = writerOptions.MaxRowCount;
        var progress = writerOptions.WriteProgress;
        var progressInterval = writerOptions.WriteProgressIntervalRows;

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

            if (progress is not null && dataRowCount % progressInterval == 0)
            {
                progress.Report(new CsvWriteProgress
                {
                    RowsWritten = dataRowCount,
                    BytesWritten = writer.CharsWritten,
                });
            }
        }

        // Report final progress
        progress?.Report(new CsvWriteProgress
        {
            RowsWritten = dataRowCount,
            BytesWritten = writer.CharsWritten,
        });
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
        if (needsEmptyColumnScan)
        {
            var materialized = await MaterializeRecordsAsync(records, writerOptions.MaxRowCount, cancellationToken).ConfigureAwait(false);
            WriteRecordsFiltered(writer, materialized, includeHeader);
            return;
        }

        int rowNumber = 0;
        int dataRowCount = 0;
        var maxRows = writerOptions.MaxRowCount;
        var progress = writerOptions.WriteProgress;
        var progressInterval = writerOptions.WriteProgressIntervalRows;

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

            if (progress is not null && dataRowCount % progressInterval == 0)
            {
                progress.Report(new CsvWriteProgress
                {
                    RowsWritten = dataRowCount,
                    BytesWritten = writer.CharsWritten,
                });
            }
        }

        progress?.Report(new CsvWriteProgress
        {
            RowsWritten = dataRowCount,
            BytesWritten = writer.CharsWritten,
        });
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
        if (needsEmptyColumnScan)
        {
            var materialized = await MaterializeRecordsAsync(records, writerOptions.MaxRowCount, cancellationToken).ConfigureAwait(false);
            await WriteRecordsFilteredAsync(writer, materialized, includeHeader, cancellationToken).ConfigureAwait(false);
            return;
        }

        int rowNumber = 0;
        int dataRowCount = 0;
        var maxRows = writerOptions.MaxRowCount;
        var progress = writerOptions.WriteProgress;
        var progressInterval = writerOptions.WriteProgressIntervalRows;

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

            if (progress is not null && dataRowCount % progressInterval == 0)
            {
                progress.Report(new CsvWriteProgress
                {
                    RowsWritten = dataRowCount,
                    BytesWritten = writer.CharsWritten,
                });
            }
        }

        progress?.Report(new CsvWriteProgress
        {
            RowsWritten = dataRowCount,
            BytesWritten = writer.CharsWritten,
        });
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
        if (needsEmptyColumnScan)
        {
            var materialized = MaterializeRecords(records, writerOptions.MaxRowCount);
            await WriteRecordsFilteredAsync(writer, materialized, includeHeader, cancellationToken).ConfigureAwait(false);
            return;
        }

        int rowNumber = 0;
        int dataRowCount = 0;
        var maxRows = writerOptions.MaxRowCount;
        var progress = writerOptions.WriteProgress;
        var progressInterval = writerOptions.WriteProgressIntervalRows;

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

            if (progress is not null && dataRowCount % progressInterval == 0)
            {
                progress.Report(new CsvWriteProgress
                {
                    RowsWritten = dataRowCount,
                    BytesWritten = writer.CharsWritten,
                });
            }
        }

        // Report final progress
        progress?.Report(new CsvWriteProgress
        {
            RowsWritten = dataRowCount,
            BytesWritten = writer.CharsWritten,
        });
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
        await writer.WriteRowWithFormatsAsync(valuesBuffer, formatsBuffer, cancellationToken).ConfigureAwait(false);
    }

    private void WriteHeaderRow(CsvStreamWriter writer)
    {
        writer.WriteRow(headerBuffer);
    }

    private bool ComputeNeedsEmptyColumnScan()
    {
        if (writerOptions.ExcludeEmptyColumns)
            return true;
        for (int i = 0; i < accessors.Length; i++)
        {
            if (accessors[i].ExcludeFromWriteIfAllEmpty)
                return true;
        }
        return false;
    }

    private static bool IsValueEmpty(object? value)
    {
        if (value is null)
            return true;
        if (value is string s)
            return s.Length == 0;
        if (value.GetType().IsValueType)
            return false; // Boxed value types (int, DateTime, etc.) never have empty ToString()
        var str = value.ToString();
        return str is null or { Length: 0 };
    }

    private int[] ScanForNonEmptyColumns(IReadOnlyList<T> records)
    {
        int columnCount = accessors.Length;
        bool globalExclude = writerOptions.ExcludeEmptyColumns;

        // Determine which columns are candidates for exclusion
        var isCandidate = new bool[columnCount];
        int candidateCount = 0;
        for (int i = 0; i < columnCount; i++)
        {
            isCandidate[i] = globalExclude || accessors[i].ExcludeFromWriteIfAllEmpty;
            if (isCandidate[i]) candidateCount++;
        }

        if (candidateCount == 0)
            return []; // No candidates → no filtering needed

        var hasNonEmpty = new bool[columnCount];
        int resolvedCandidateCount = 0;
        bool anyRecordScanned = false;

        foreach (var record in records)
        {
            if (record is null)
                continue;

            anyRecordScanned = true;

            for (int i = 0; i < columnCount; i++)
            {
                if (!isCandidate[i] || hasNonEmpty[i])
                    continue;

                object? value;
                try
                {
                    value = accessors[i].GetValue(record);
                }
                catch
                {
                    // Failed getter → treat as empty for scan purposes
                    continue;
                }

                if (!IsValueEmpty(value))
                {
                    hasNonEmpty[i] = true;
                    resolvedCandidateCount++;
                    if (resolvedCandidateCount == candidateCount)
                        return []; // All candidate columns are non-empty → no filtering needed
                }
            }
        }

        // No scannable records (empty list or all-null records) → no filtering, write full header
        if (!anyRecordScanned)
            return [];

        // Build result: include non-candidate columns (always) + non-empty candidate columns
        int includeCount = 0;
        for (int i = 0; i < columnCount; i++)
        {
            if (!isCandidate[i] || hasNonEmpty[i])
                includeCount++;
        }

        if (includeCount == columnCount)
            return []; // All columns included → no filtering

        if (includeCount == 0)
            return [-1]; // Sentinel: all columns excluded

        var indices = new int[includeCount];
        int idx = 0;
        for (int i = 0; i < columnCount; i++)
        {
            if (!isCandidate[i] || hasNonEmpty[i])
                indices[idx++] = i;
        }
        return indices;
    }

    private void WriteFilteredHeader(CsvStreamWriter writer, int[] columnIndices)
    {
        var filteredHeaders = new string[columnIndices.Length];
        for (int i = 0; i < columnIndices.Length; i++)
            filteredHeaders[i] = headerBuffer[columnIndices[i]];
        writer.WriteRow(filteredHeaders);
    }

    private void WriteFilteredRecord(CsvStreamWriter writer, T record, int rowNumber, int[] columnIndices, object?[] filteredValues, string?[] filteredFormats)
    {
        if (record is null)
        {
            writer.EndRow();
            return;
        }

        for (int i = 0; i < columnIndices.Length; i++)
        {
            int ci = columnIndices[i];
            try
            {
                filteredValues[i] = accessors[ci].GetValue(record);
            }
            catch (Exception ex)
            {
                if (writerOptions.OnSerializeError is not null)
                {
                    var context = new CsvSerializeErrorContext
                    {
                        Row = rowNumber,
                        Column = ci + 1,
                        MemberName = accessors[ci].MemberName,
                        SourceType = typeof(T),
                        Value = null,
                        Exception = ex
                    };

                    var action = writerOptions.OnSerializeError(context);
                    switch (action)
                    {
                        case SerializeErrorAction.SkipRow:
                            return;
                        case SerializeErrorAction.WriteNull:
                            filteredValues[i] = null;
                            continue;
                        case SerializeErrorAction.Throw:
                        default:
                            break;
                    }
                }

                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row {rowNumber}, Column {ci + 1}: Failed to get value for member '{accessors[ci].MemberName}': {ex.Message}",
                    ex);
            }
        }
        writer.WriteRowWithFormats(filteredValues, filteredFormats);
    }

    private async ValueTask WriteFilteredRecordAsync(CsvAsyncStreamWriter writer, T record, int rowNumber, int[] columnIndices, object?[] filteredValues, string?[] filteredFormats, CancellationToken cancellationToken)
    {
        if (record is null)
        {
            await writer.EndRowAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        for (int i = 0; i < columnIndices.Length; i++)
        {
            int ci = columnIndices[i];
            try
            {
                filteredValues[i] = accessors[ci].GetValue(record);
            }
            catch (Exception ex)
            {
                if (writerOptions.OnSerializeError is not null)
                {
                    var context = new CsvSerializeErrorContext
                    {
                        Row = rowNumber,
                        Column = ci + 1,
                        MemberName = accessors[ci].MemberName,
                        SourceType = typeof(T),
                        Value = null,
                        Exception = ex
                    };

                    var action = writerOptions.OnSerializeError(context);
                    switch (action)
                    {
                        case SerializeErrorAction.SkipRow:
                            return;
                        case SerializeErrorAction.WriteNull:
                            filteredValues[i] = null;
                            continue;
                        case SerializeErrorAction.Throw:
                        default:
                            break;
                    }
                }

                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row {rowNumber}, Column {ci + 1}: Failed to get value for member '{accessors[ci].MemberName}': {ex.Message}",
                    ex);
            }
        }
        await writer.WriteRowWithFormatsAsync(filteredValues, filteredFormats, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteFilteredHeaderAsync(CsvAsyncStreamWriter writer, int[] columnIndices, CancellationToken cancellationToken)
    {
        var filteredHeaders = new string[columnIndices.Length];
        for (int i = 0; i < columnIndices.Length; i++)
            filteredHeaders[i] = headerBuffer[columnIndices[i]];
        await writer.WriteRowAsync(filteredHeaders, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<T> MaterializeRecords(IEnumerable<T> records, int? maxRowCount)
    {
        if (records is IReadOnlyList<T> list)
        {
            if (maxRowCount.HasValue && list.Count > maxRowCount.Value)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Exceeded maximum row count of {maxRowCount.Value}");
            }
            return list;
        }

        var materialized = new List<T>();
        foreach (var record in records)
        {
            materialized.Add(record);
            if (maxRowCount.HasValue && materialized.Count > maxRowCount.Value)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Exceeded maximum row count of {maxRowCount.Value}");
            }
        }
        return materialized;
    }

    private static async ValueTask<IReadOnlyList<T>> MaterializeRecordsAsync(IAsyncEnumerable<T> records, int? maxRowCount, CancellationToken cancellationToken)
    {
        var materialized = new List<T>();
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            materialized.Add(record);
            if (maxRowCount.HasValue && materialized.Count > maxRowCount.Value)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Exceeded maximum row count of {maxRowCount.Value}");
            }
        }
        return materialized;
    }

    private void WriteRecordsFiltered(CsvStreamWriter writer, IReadOnlyList<T> records, bool includeHeader)
    {
        var columnIndices = ScanForNonEmptyColumns(records);

        // All columns non-empty → no filtering, use normal path
        if (columnIndices.Length == 0)
        {
            WriteRecordsUnfiltered(writer, records, includeHeader);
            return;
        }

        // All columns empty → write nothing
        if (columnIndices is [-1])
            return;

        // Pre-allocate reusable buffers for the filtered write loop
        var filteredValues = new object?[columnIndices.Length];
        var filteredFormats = new string?[columnIndices.Length];
        for (int i = 0; i < columnIndices.Length; i++)
            filteredFormats[i] = formatsBuffer[columnIndices[i]];

        var progress = writerOptions.WriteProgress;
        var progressInterval = writerOptions.WriteProgressIntervalRows;
        int rowNumber = 0;

        if (includeHeader && writerOptions.WriteHeader)
        {
            WriteFilteredHeader(writer, columnIndices);
            rowNumber++;
        }

        for (int r = 0; r < records.Count; r++)
        {
            rowNumber++;
            WriteFilteredRecord(writer, records[r], rowNumber, columnIndices, filteredValues, filteredFormats);

            if (progress is not null && (r + 1) % progressInterval == 0)
            {
                progress.Report(new CsvWriteProgress
                {
                    RowsWritten = r + 1,
                    BytesWritten = writer.CharsWritten,
                });
            }
        }

        progress?.Report(new CsvWriteProgress
        {
            RowsWritten = records.Count,
            BytesWritten = writer.CharsWritten,
        });
    }

    private void WriteRecordsUnfiltered(CsvStreamWriter writer, IReadOnlyList<T> records, bool includeHeader)
    {
        int rowNumber = 0;
        var maxRows = writerOptions.MaxRowCount;
        var progress = writerOptions.WriteProgress;
        var progressInterval = writerOptions.WriteProgressIntervalRows;

        if (includeHeader && writerOptions.WriteHeader)
        {
            WriteHeaderRow(writer);
            rowNumber++;
        }

        for (int r = 0; r < records.Count; r++)
        {
            rowNumber++;

            if (maxRows.HasValue && (r + 1) > maxRows.Value)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Exceeded maximum row count of {maxRows.Value}");
            }

            WriteRecordInternal(writer, records[r], rowNumber);

            if (progress is not null && (r + 1) % progressInterval == 0)
            {
                progress.Report(new CsvWriteProgress
                {
                    RowsWritten = r + 1,
                    BytesWritten = writer.CharsWritten,
                });
            }
        }

        progress?.Report(new CsvWriteProgress
        {
            RowsWritten = records.Count,
            BytesWritten = writer.CharsWritten,
        });
    }

    private async ValueTask WriteRecordsFilteredAsync(
        CsvAsyncStreamWriter writer,
        IReadOnlyList<T> records,
        bool includeHeader,
        CancellationToken cancellationToken)
    {
        var columnIndices = ScanForNonEmptyColumns(records);

        if (columnIndices.Length == 0)
        {
            await WriteRecordsUnfilteredAsync(writer, records, includeHeader, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (columnIndices is [-1])
            return;

        // Pre-allocate reusable buffers for the filtered write loop
        var filteredValues = new object?[columnIndices.Length];
        var filteredFormats = new string?[columnIndices.Length];
        for (int i = 0; i < columnIndices.Length; i++)
            filteredFormats[i] = formatsBuffer[columnIndices[i]];

        var progress = writerOptions.WriteProgress;
        var progressInterval = writerOptions.WriteProgressIntervalRows;
        int rowNumber = 0;

        if (includeHeader && writerOptions.WriteHeader)
        {
            await WriteFilteredHeaderAsync(writer, columnIndices, cancellationToken).ConfigureAwait(false);
            rowNumber++;
        }

        for (int r = 0; r < records.Count; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            await WriteFilteredRecordAsync(writer, records[r], rowNumber, columnIndices, filteredValues, filteredFormats, cancellationToken).ConfigureAwait(false);

            if (progress is not null && (r + 1) % progressInterval == 0)
            {
                progress.Report(new CsvWriteProgress
                {
                    RowsWritten = r + 1,
                    BytesWritten = writer.CharsWritten,
                });
            }
        }

        progress?.Report(new CsvWriteProgress
        {
            RowsWritten = records.Count,
            BytesWritten = writer.CharsWritten,
        });
    }

    private async ValueTask WriteRecordsUnfilteredAsync(
        CsvAsyncStreamWriter writer,
        IReadOnlyList<T> records,
        bool includeHeader,
        CancellationToken cancellationToken)
    {
        int rowNumber = 0;
        var maxRows = writerOptions.MaxRowCount;
        var progress = writerOptions.WriteProgress;
        var progressInterval = writerOptions.WriteProgressIntervalRows;

        if (includeHeader && writerOptions.WriteHeader)
        {
            await writer.WriteRowAsync(headerBuffer, cancellationToken).ConfigureAwait(false);
            rowNumber++;
        }

        for (int r = 0; r < records.Count; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;

            if (maxRows.HasValue && (r + 1) > maxRows.Value)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Exceeded maximum row count of {maxRows.Value}");
            }

            await WriteRecordInternalAsync(writer, records[r], rowNumber, cancellationToken).ConfigureAwait(false);

            if (progress is not null && (r + 1) % progressInterval == 0)
            {
                progress.Report(new CsvWriteProgress
                {
                    RowsWritten = r + 1,
                    BytesWritten = writer.CharsWritten,
                });
            }
        }

        progress?.Report(new CsvWriteProgress
        {
            RowsWritten = records.Count,
            BytesWritten = writer.CharsWritten,
        });
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
            var headerName = !string.IsNullOrWhiteSpace(attribute?.Name) ? attribute.Name : property.Name;
            var format = attribute?.Format;

            accessors[i] = new PropertyAccessor(
                property.Name,
                headerName,
                format,
                attribute?.ExcludeFromWriteIfAllEmpty ?? false,
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

    private sealed class PropertyAccessor(string memberName, string headerName, string? format, bool excludeFromWriteIfAllEmpty, Func<object, object?> getter)
    {
        public string MemberName { get; } = memberName;
        public string HeaderName { get; } = headerName;
        public string? Format { get; } = format;
        public bool ExcludeFromWriteIfAllEmpty { get; } = excludeFromWriteIfAllEmpty;
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
    private static readonly ConcurrentDictionary<Type, Func<CsvWriteOptions?, object>> generatedFactories = new();

    static CsvRecordWriterFactory()
    {
        RegisterGeneratedWriters(generatedFactories);
    }

    /// <summary>
    /// Creates a new record writer for the specified type and options.
    /// Prefers generated writers when available, falling back to reflection-based writers.
    /// </summary>
    public static CsvRecordWriter<T> GetWriter<T>(CsvWriteOptions? options = null)
    {
        options ??= CsvWriteOptions.Default;

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
    public static bool TryGetWriter<T>(CsvWriteOptions? options, out CsvRecordWriter<T>? writer)
    {
        if (generatedFactories.TryGetValue(typeof(T), out var factory))
        {
            options ??= CsvWriteOptions.Default;
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
    public static void RegisterGeneratedWriter(Type type, Func<CsvWriteOptions?, object> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);

        generatedFactories[type] = factory;
    }

    /// <summary>
    /// Populated by the source generator; becomes a no-op when no generators run.
    /// </summary>
    /// <param name="factories">Cache to register writer factories into.</param>
    static partial void RegisterGeneratedWriters(ConcurrentDictionary<Type, Func<CsvWriteOptions?, object>> factories);
}

