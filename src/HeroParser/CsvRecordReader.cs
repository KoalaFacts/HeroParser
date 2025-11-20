using HeroParser.SeparatedValues;

namespace HeroParser;

/// <summary>
/// Streams CSV rows as strongly typed records using the buffered text-based reader.
/// </summary>
public ref struct CsvRecordReader<T> where T : class, new()
{
    private CsvCharSpanReader reader;
    private readonly CsvRecordBinder<T> binder;
    private T current;
    private int rowNumber;

    internal CsvRecordReader(CsvCharSpanReader reader, CsvRecordBinder<T> binder)
    {
        this.reader = reader;
        this.binder = binder;
        current = default!;
        rowNumber = 0;
    }

    /// <summary>Gets the current mapped record.</summary>
    public T Current => current;

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

            if (binder.NeedsHeaderResolution)
            {
                binder.BindHeader(row, rowNumber);
                continue;
            }

            current = binder.Bind(row, rowNumber);
            return true;
        }

        current = default!;
        return false;
    }

    /// <summary>Releases pooled buffers from the underlying CSV reader.</summary>
    public readonly void Dispose() => reader.Dispose();
}
