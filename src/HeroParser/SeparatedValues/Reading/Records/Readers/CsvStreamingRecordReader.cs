using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Records.Binding;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser.SeparatedValues.Reading.Records.Readers;

/// <summary>
/// Streams CSV rows from <see cref="CsvStreamReader"/> into strongly typed records.
/// </summary>
public ref struct CsvStreamingRecordReader<T> where T : class, new()
{
    private CsvStreamReader reader;
    private readonly CsvRecordBinder<T>? legacyBinder;
    private readonly ICsvBinder<T>? typedBinder;
    private readonly int skipRows;
    private readonly IProgress<CsvProgress>? progress;
    private readonly int progressInterval;
    private readonly long totalBytes;
    private int rowNumber;
    private int skippedCount;
    private int dataRowCount;

    internal CsvStreamingRecordReader(CsvStreamReader reader, CsvRecordBinder<T> binder, int skipRows = 0,
        IProgress<CsvProgress>? progress = null, int progressInterval = 1000, long totalBytes = -1)
    {
        this.reader = reader;
        legacyBinder = binder;
        typedBinder = null;
        this.skipRows = skipRows;
        this.progress = progress;
        this.progressInterval = progressInterval > 0 ? progressInterval : 1000;
        this.totalBytes = totalBytes;
        Current = default!;
        rowNumber = 0;
        skippedCount = 0;
        dataRowCount = 0;
    }

    internal CsvStreamingRecordReader(CsvStreamReader reader, ICsvBinder<T> typedBinder, int skipRows = 0,
        IProgress<CsvProgress>? progress = null, int progressInterval = 1000, long totalBytes = -1)
    {
        this.reader = reader;
        legacyBinder = null;
        this.typedBinder = typedBinder;
        this.skipRows = skipRows;
        this.progress = progress;
        this.progressInterval = progressInterval > 0 ? progressInterval : 1000;
        this.totalBytes = totalBytes;
        Current = default!;
        rowNumber = 0;
        skippedCount = 0;
        dataRowCount = 0;
    }

    /// <summary>Gets the current mapped record.</summary>
    public T Current { get; private set; }

    /// <summary>Returns this instance for <c>foreach</c> support.</summary>
    public readonly CsvStreamingRecordReader<T> GetEnumerator() => this;

    /// <summary>
    /// Advances to the next mapped record, binding the row to <typeparamref name="T"/>.
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
                BytesProcessed = reader.BytesRead,
                TotalBytes = totalBytes
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
                BytesProcessed = reader.BytesRead,
                TotalBytes = totalBytes
            });
        }
    }

    /// <summary>Releases pooled buffers and optionally disposes the underlying stream.</summary>
    public readonly void Dispose() => reader.Dispose();
}
