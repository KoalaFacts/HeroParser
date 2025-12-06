using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.SeparatedValues.Reading.Records;

/// <summary>
/// Reads CSV rows as strongly typed records from an in-memory span.
/// </summary>
/// <typeparam name="TElement">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
/// <typeparam name="T">The record type to deserialize.</typeparam>
public ref struct CsvRecordReader<TElement, T>
    where TElement : unmanaged, IEquatable<TElement>
    where T : class, new()
{
    private CsvRowReader<TElement> reader;
    private readonly ICsvBinder<TElement, T> binder;
    private readonly int skipRows;
    private readonly IProgress<CsvProgress>? progress;
    private readonly int progressInterval;
    private int rowNumber;
    private int skippedCount;
    private int dataRowCount;

    internal CsvRecordReader(CsvRowReader<TElement> reader, ICsvBinder<TElement, T> binder, int skipRows = 0,
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
    public readonly CsvRecordReader<TElement, T> GetEnumerator() => this;

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
