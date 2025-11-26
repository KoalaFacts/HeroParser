namespace HeroParser.SeparatedValues.Records.Readers;

/// <summary>
/// Streams CSV rows as strongly typed records using the buffered text-based reader.
/// </summary>
public ref struct CsvRecordReader<T> where T : class, new()
{
    private CsvCharSpanReader reader;
    private readonly CsvRecordBinder<T> binder;
    private readonly int skipRows;
    private int rowNumber;
    private int skippedCount;

    internal CsvRecordReader(CsvCharSpanReader reader, CsvRecordBinder<T> binder, int skipRows = 0)
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

            Current = result;
            return true;
        }

        Current = default!;
        return false;
    }

    /// <summary>Releases pooled buffers from the underlying CSV reader.</summary>
    public readonly void Dispose() => reader.Dispose();
}
