namespace HeroParser.Excels.Writing;

/// <summary>
/// Represents progress information reported during Excel writing operations.
/// </summary>
public readonly struct ExcelWriteProgress
{
    /// <summary>
    /// Gets the number of data rows written so far.
    /// </summary>
    public int RowsWritten { get; init; }

    /// <summary>
    /// Gets the name of the worksheet currently being written.
    /// </summary>
    public string SheetName { get; init; }
}
