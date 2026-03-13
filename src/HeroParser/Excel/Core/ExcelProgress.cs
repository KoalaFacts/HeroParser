namespace HeroParser.Excel.Core;

/// <summary>
/// Reports progress during Excel reading operations.
/// </summary>
public readonly struct ExcelProgress(int rowsRead, string sheetName)
{
    /// <summary>Number of data rows read so far.</summary>
    public int RowsRead { get; } = rowsRead;

    /// <summary>Name of the sheet being read.</summary>
    public string SheetName { get; } = sheetName;
}
