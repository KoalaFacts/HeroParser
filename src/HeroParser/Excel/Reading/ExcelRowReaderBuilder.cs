using HeroParser.Excels.Xlsx;

namespace HeroParser.Excels.Reading;

/// <summary>
/// Fluent builder for reading Excel rows as string arrays without typed record binding.
/// </summary>
public sealed class ExcelRowReaderBuilder
{
    private string? sheetName;
    private int? sheetIndex;
    private int skipRows;
    private bool hasHeaderRow = true;

    internal ExcelRowReaderBuilder() { }

    /// <summary>
    /// Selects the sheet to read by name.
    /// </summary>
    /// <param name="name">The name of the sheet to read.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRowReaderBuilder FromSheet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        sheetName = name;
        sheetIndex = null;
        return this;
    }

    /// <summary>
    /// Selects the sheet to read by zero-based index.
    /// </summary>
    /// <param name="index">The zero-based index of the sheet to read.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRowReaderBuilder FromSheet(int index)
    {
        sheetIndex = index;
        sheetName = null;
        return this;
    }

    /// <summary>
    /// Skips the specified number of rows before reading the header or data.
    /// </summary>
    /// <param name="count">The number of rows to skip.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRowReaderBuilder SkipRows(int count)
    {
        skipRows = count;
        return this;
    }

    /// <summary>
    /// Indicates that the Excel data does not include a header row.
    /// All rows (including the first) will be returned as data.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRowReaderBuilder WithoutHeader()
    {
        hasHeaderRow = false;
        return this;
    }

    /// <summary>
    /// Configures multi-sheet reading with different record types per sheet.
    /// </summary>
    /// <typeparam name="TSheet">The record type for the specified sheet.</typeparam>
    /// <param name="sheetName">The name of the sheet to read.</param>
    /// <returns>A multi-sheet builder for configuring additional sheets.</returns>
    public ExcelMultiSheetBuilder WithSheet<TSheet>(string sheetName) where TSheet : new()
    {
        var builder = new ExcelMultiSheetBuilder();
        return builder.WithSheet<TSheet>(sheetName);
    }

    /// <summary>
    /// Reads rows from an Excel file on disk.
    /// </summary>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <returns>A list of string arrays, one per data row.</returns>
    public List<string[]> FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return FromStream(stream);
    }

    /// <summary>
    /// Reads rows from a stream containing .xlsx data.
    /// </summary>
    /// <param name="stream">A seekable stream containing .xlsx data.</param>
    /// <returns>A list of string arrays, one per data row.</returns>
    public List<string[]> FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var xlsxReader = new XlsxReader(stream);
        var sheet = ResolveSheet(xlsxReader.Workbook);
        using var sheetReader = xlsxReader.OpenSheet(sheet);

        // Skip configured rows
        for (int i = 0; i < skipRows; i++)
        {
            if (sheetReader.ReadNextRow() is null)
                return [];
        }

        // Skip header row if configured
        if (hasHeaderRow)
        {
            if (sheetReader.ReadNextRow() is null)
                return [];
        }

        var results = new List<string[]>();

        while (true)
        {
            var cells = sheetReader.ReadNextRow();
            if (cells is null)
                break;

            results.Add(cells);
        }

        return results;
    }

    private XlsxWorkbook.SheetInfo ResolveSheet(XlsxWorkbook workbook)
    {
        if (sheetName is not null)
            return workbook.GetSheetByName(sheetName);

        if (sheetIndex.HasValue)
            return workbook.GetSheetByIndex(sheetIndex.Value);

        return workbook.GetFirstSheet();
    }
}
