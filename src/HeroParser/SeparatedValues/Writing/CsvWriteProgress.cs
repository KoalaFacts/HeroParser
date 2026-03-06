namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// Represents progress information during CSV writing operations.
/// </summary>
public readonly struct CsvWriteProgress
{
    /// <summary>
    /// Gets the number of data rows written so far.
    /// </summary>
    public long RowsWritten { get; init; }

    /// <summary>
    /// Gets the approximate number of bytes (characters) written so far.
    /// </summary>
    public long BytesWritten { get; init; }
}
