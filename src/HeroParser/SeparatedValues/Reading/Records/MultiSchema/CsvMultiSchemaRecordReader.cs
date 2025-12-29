using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.SeparatedValues.Reading.Records.MultiSchema;

/// <summary>
/// Reads multi-schema CSV rows from an in-memory span, yielding records of different types
/// based on a discriminator column value.
/// </summary>
/// <typeparam name="TElement">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
/// <remarks>
/// <para>
/// This is a ref struct optimized for stack allocation. Use <c>foreach</c> to iterate over records.
/// The Current property returns <see cref="object"/> which can be pattern-matched to the specific record type.
/// </para>
/// <para>
/// Thread-Safety: This type is not thread-safe. Each instance should be used on a single thread.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// foreach (var record in reader)
/// {
///     switch (record)
///     {
///         case HeaderRecord h: Console.WriteLine(h.FileId); break;
///         case DetailRecord d: Console.WriteLine(d.Amount); break;
///         case TrailerRecord t: Console.WriteLine(t.Total); break;
///     }
/// }
/// </code>
/// </example>
public ref struct CsvMultiSchemaRecordReader<TElement>
    where TElement : unmanaged, IEquatable<TElement>
{
    private CsvRowReader<TElement> reader;
    private readonly CsvMultiSchemaBinder<TElement> binder;
    private readonly int skipRows;
    private readonly IProgress<CsvProgress>? progress;
    private readonly int progressInterval;
    private int rowNumber;
    private int skippedCount;
    private int dataRowCount;

    /// <summary>
    /// Gets the current record. Valid after <see cref="MoveNext"/> returns <see langword="true"/>.
    /// </summary>
    public object Current { get; private set; }

    internal CsvMultiSchemaRecordReader(
        CsvRowReader<TElement> rowReader,
        CsvMultiSchemaBinder<TElement> schemaBinder,
        int skipRowCount = 0,
        IProgress<CsvProgress>? progressReporter = null,
        int progressIntervalRows = 1000)
    {
        reader = rowReader;
        binder = schemaBinder;
        skipRows = skipRowCount;
        progress = progressReporter;
        progressInterval = progressIntervalRows > 0 ? progressIntervalRows : 1000;
        Current = null!;
        rowNumber = 0;
        skippedCount = 0;
        dataRowCount = 0;
    }

    /// <summary>
    /// Returns this instance for <c>foreach</c> support.
    /// </summary>
    public readonly CsvMultiSchemaRecordReader<TElement> GetEnumerator() => this;

    /// <summary>
    /// Advances to the next record, parsing and binding the underlying row to the appropriate type.
    /// </summary>
    /// <returns><see langword="true"/> if a record was successfully read; otherwise, <see langword="false"/>.</returns>
    public bool MoveNext()
    {
        while (reader.MoveNext())
        {
            rowNumber++;
            var row = reader.Current;

            // Skip initial rows if requested
            if (skippedCount < skipRows)
            {
                skippedCount++;
                continue;
            }

            // Handle header resolution
            if (binder.NeedsHeaderResolution)
            {
                binder.BindHeader(row, rowNumber);
                continue;
            }

            // Bind the row to the appropriate record type
            var result = binder.Bind(row, rowNumber);
            if (result is null)
            {
                // Row was skipped (unmatched or filtered)
                continue;
            }

            dataRowCount++;
            ReportProgress();

            Current = result;
            return true;
        }

        ReportFinalProgress();
        Current = null!;
        return false;
    }

    private void ReportProgress()
    {
        if (progress is not null && dataRowCount % progressInterval == 0)
        {
            progress.Report(new CsvProgress
            {
                RowsProcessed = dataRowCount,
                BytesProcessed = 0, // Not available for span-based parsing
                TotalBytes = -1
            });
        }
    }

    private void ReportFinalProgress()
    {
        if (progress is not null && dataRowCount > 0)
        {
            progress.Report(new CsvProgress
            {
                RowsProcessed = dataRowCount,
                BytesProcessed = 0,
                TotalBytes = -1
            });
        }
    }

    /// <summary>
    /// Releases pooled buffers from the underlying CSV reader.
    /// </summary>
    public readonly void Dispose() => reader.Dispose();
}
