using HeroParser.SeparatedValues.Records.Binding;

namespace HeroParser.SeparatedValues.Records.Readers;

/// <summary>
/// Streams CSV rows as strongly typed records using the buffered text-based reader.
/// </summary>
public ref struct CsvRecordReader<T> where T : class, new()
{
    private CsvCharSpanReader reader;
    private readonly CsvRecordBinder<T>? legacyBinder;
    private readonly ICsvTypedBinder<T>? typedBinder;
    private readonly int skipRows;
    private readonly IProgress<CsvProgress>? progress;
    private readonly int progressInterval;
    private int rowNumber;
    private int skippedCount;
    private int dataRowCount;

    internal CsvRecordReader(CsvCharSpanReader reader, CsvRecordBinder<T> binder, int skipRows = 0,
        IProgress<CsvProgress>? progress = null, int progressInterval = 1000)
    {
        this.reader = reader;
        legacyBinder = binder;
        typedBinder = null;
        this.skipRows = skipRows;
        this.progress = progress;
        this.progressInterval = progressInterval > 0 ? progressInterval : 1000;
        Current = default!;
        rowNumber = 0;
        skippedCount = 0;
        dataRowCount = 0;
    }

    internal CsvRecordReader(CsvCharSpanReader reader, ICsvTypedBinder<T> typedBinder, int skipRows = 0,
        IProgress<CsvProgress>? progress = null, int progressInterval = 1000)
    {
        this.reader = reader;
        legacyBinder = null;
        this.typedBinder = typedBinder;
        this.skipRows = skipRows;
        this.progress = progress;
        this.progressInterval = progressInterval > 0 ? progressInterval : 1000;
        Current = default!;
        rowNumber = 0;
        skippedCount = 0;
        dataRowCount = 0;
    }

    /// <summary>Gets the current mapped record.</summary>
    public T Current { get; private set; }

    /// <summary>Returns this instance for <c>foreach</c> support.</summary>
    public readonly CsvRecordReader<T> GetEnumerator() => this;

    /// <summary>
    /// Advances to the next mapped record, parsing and binding the underlying row.
    /// </summary>
    public bool MoveNext()
    {
        // Use typed binder for boxing-free performance when available
        if (typedBinder is not null)
            return MoveNextTyped();

        return MoveNextLegacy();
    }

    private bool MoveNextTyped()
    {
        while (reader.MoveNext())
        {
            rowNumber++;
            var row = reader.Current;

            // Skip rows if requested
            if (skippedCount < skipRows)
            {
                skippedCount++;
                continue;
            }

            if (typedBinder!.NeedsHeaderResolution)
            {
                typedBinder.BindHeader(row, rowNumber);
                continue;
            }

            var result = typedBinder.Bind(row, rowNumber);
            if (result is null)
            {
                // Row was skipped due to error handling
                continue;
            }

            dataRowCount++;
            ReportProgress();

            Current = result;
            return true;
        }

        ReportFinalProgress();
        Current = default!;
        return false;
    }

    private bool MoveNextLegacy()
    {
        while (reader.MoveNext())
        {
            rowNumber++;
            var row = reader.Current;

            // Skip rows if requested
            if (skippedCount < skipRows)
            {
                skippedCount++;
                continue;
            }

            if (legacyBinder!.NeedsHeaderResolution)
            {
                legacyBinder.BindHeader(row, rowNumber);
                continue;
            }

            var result = legacyBinder.Bind(row, rowNumber);
            if (result is null)
            {
                // Row was skipped due to error handling
                continue;
            }

            dataRowCount++;
            ReportProgress();

            Current = result;
            return true;
        }

        ReportFinalProgress();
        Current = default!;
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

    /// <summary>Releases pooled buffers from the underlying CSV reader.</summary>
    public readonly void Dispose() => reader.Dispose();
}
