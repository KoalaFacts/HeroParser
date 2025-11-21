using HeroParser.SeparatedValues.Streaming;

namespace HeroParser.SeparatedValues.Records.Readers;

/// <summary>
/// Streams CSV rows from <see cref="CsvStreamReader"/> into strongly typed records.
/// </summary>
public ref struct CsvStreamingRecordReader<T> where T : class, new()
{
    private CsvStreamReader reader;
    private readonly CsvRecordBinder<T> binder;
    private int rowNumber;

    internal CsvStreamingRecordReader(CsvStreamReader reader, CsvRecordBinder<T> binder)
    {
        this.reader = reader;
        this.binder = binder;
        Current = default!;
        rowNumber = 0;
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

            if (binder.NeedsHeaderResolution)
            {
                binder.BindHeader(row, rowNumber);
                continue;
            }

            Current = binder.Bind(row, rowNumber);
            return true;
        }

        Current = default!;
        return false;
    }

    /// <summary>Releases pooled buffers and optionally disposes the underlying stream.</summary>
    public readonly void Dispose() => reader.Dispose();
}
