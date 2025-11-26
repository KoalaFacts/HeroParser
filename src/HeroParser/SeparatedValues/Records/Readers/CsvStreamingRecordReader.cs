using HeroParser.SeparatedValues.Streaming;

namespace HeroParser.SeparatedValues.Records.Readers;

/// <summary>
/// Streams CSV rows from <see cref="CsvStreamReader"/> into strongly typed records.
/// </summary>
public ref struct CsvStreamingRecordReader<T> where T : class, new()
{
    private CsvStreamReader reader;
    private readonly CsvRecordBinder<T> binder;
    private readonly int skipRows;
    private int rowNumber;
    private int skippedCount;

    internal CsvStreamingRecordReader(CsvStreamReader reader, CsvRecordBinder<T> binder, int skipRows = 0)
    {
        this.reader = reader;
        this.binder = binder;
        this.skipRows = skipRows;
        Current = default!;
        rowNumber = 0;
        skippedCount = 0;
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

            Current = result;
            return true;
        }

        Current = default!;
        return false;
    }

    /// <summary>Releases pooled buffers and optionally disposes the underlying stream.</summary>
    public readonly void Dispose() => reader.Dispose();
}
