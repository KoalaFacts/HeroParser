namespace HeroParser.SeparatedValues.Records.Readers;

/// <summary>
/// Streams CSV rows as strongly typed records using the buffered text-based reader.
/// </summary>
public ref struct CsvRecordReader<T> where T : class, new()
{
    private CsvCharSpanReader reader;
    private readonly CsvRecordBinder<T> binder;
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
        this.binder = binder;
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

            if (binder.NeedsHeaderResolution)
            {
                binder.BindHeader(row, rowNumber);
                continue;
            }

            var result = binder.Bind(row, rowNumber);
            if (result is null)
            {
                // Row was skipped due to error handling
                continue;
            }

            dataRowCount++;

            // Report progress at intervals
            if (progress is not null && dataRowCount % progressInterval == 0)
            {
                progress.Report(new CsvProgress
                {
                    RowsProcessed = dataRowCount,
                    BytesProcessed = 0, // Not available for span-based parsing
                    TotalBytes = -1
                });
            }

            Current = result;
            return true;
        }

        // Report final progress
        if (progress is not null && dataRowCount > 0)
        {
            progress.Report(new CsvProgress
            {
                RowsProcessed = dataRowCount,
                BytesProcessed = 0,
                TotalBytes = -1
            });
        }

        Current = default!;
        return false;
    }

    /// <summary>Releases pooled buffers from the underlying CSV reader.</summary>
    public readonly void Dispose() => reader.Dispose();
}
