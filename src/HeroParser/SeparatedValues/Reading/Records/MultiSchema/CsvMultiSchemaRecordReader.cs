using HeroParser.SeparatedValues.Reading.Span;

namespace HeroParser.SeparatedValues.Reading.Records.MultiSchema;

/// <summary>
/// Streams CSV rows as heterogeneous records using the buffered text-based reader.
/// </summary>
/// <remarks>
/// This ref struct provides foreach support for iterating over records where
/// different rows map to different types based on a discriminator column.
/// </remarks>
public ref struct CsvMultiSchemaRecordReader
{
    private CsvCharSpanReader reader;
    private readonly CsvMultiSchemaBinder binder;
    private readonly int skipRows;
    private int rowNumber;
    private int skippedCount;

    internal CsvMultiSchemaRecordReader(
        CsvCharSpanReader reader,
        CsvMultiSchemaBinder binder,
        int skipRows = 0)
    {
        this.reader = reader;
        this.binder = binder;
        this.skipRows = skipRows;
        rowNumber = 0;
        skippedCount = 0;
        Current = null!;
    }

    /// <summary>
    /// Gets the current mapped record.
    /// </summary>
    /// <remarks>
    /// Use pattern matching to determine the actual record type:
    /// <code>
    /// switch (reader.Current)
    /// {
    ///     case HeaderRecord h: ...
    ///     case DetailRecord d: ...
    /// }
    /// </code>
    /// </remarks>
    public object Current { get; private set; }

    /// <summary>
    /// Returns this instance for <c>foreach</c> support.
    /// </summary>
    public readonly CsvMultiSchemaRecordReader GetEnumerator() => this;

    /// <summary>
    /// Advances to the next mapped record, parsing and binding the underlying row.
    /// </summary>
    /// <returns><see langword="true"/> if a record was read; <see langword="false"/> if the end of data was reached.</returns>
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

            // Process header if needed
            if (binder.NeedsHeaderResolution)
            {
                binder.BindHeader(row, rowNumber);
                continue;
            }

            var result = binder.Bind(row, rowNumber);
            if (result is null)
            {
                // Row was skipped (unmatched with Skip behavior, or error handling)
                continue;
            }

            Current = result;
            return true;
        }

        Current = null!;
        return false;
    }

    /// <summary>
    /// Releases pooled buffers from the underlying CSV reader.
    /// </summary>
    public readonly void Dispose() => reader.Dispose();
}
