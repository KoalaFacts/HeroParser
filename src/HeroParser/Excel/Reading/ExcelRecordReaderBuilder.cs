using System.Globalization;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;
using HeroParser.SeparatedValues.Reading.Binders;

namespace HeroParser.Excels.Reading;

/// <summary>
/// Fluent builder for configuring and executing typed Excel reading operations.
/// </summary>
/// <typeparam name="T">The record type to deserialize rows into.</typeparam>
public sealed class ExcelRecordReaderBuilder<T> where T : new()
{
    private string? sheetName;
    private int? sheetIndex;
    private bool hasHeaderRow = true;
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private int? maxRows;
    private int skipRows;
    private IProgress<ExcelProgress>? progress;

    internal ExcelRecordReaderBuilder() { }

    /// <summary>
    /// Selects the sheet to read by name.
    /// </summary>
    /// <param name="name">The name of the sheet to read.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> FromSheet(string name)
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
    public ExcelRecordReaderBuilder<T> FromSheet(int index)
    {
        sheetIndex = index;
        sheetName = null;
        return this;
    }

    /// <summary>
    /// Indicates that the Excel data does not include a header row.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithoutHeader()
    {
        hasHeaderRow = false;
        return this;
    }

    /// <summary>
    /// Sets the culture for parsing cell values.
    /// </summary>
    /// <param name="culture">The culture to use for parsing.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithCulture(CultureInfo culture)
    {
        this.culture = culture ?? CultureInfo.InvariantCulture;
        return this;
    }

    /// <summary>
    /// Limits the maximum number of data rows to read.
    /// </summary>
    /// <param name="maxRows">The maximum number of data rows (excluding header).</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithMaxRows(int maxRows)
    {
        this.maxRows = maxRows;
        return this;
    }

    /// <summary>
    /// Skips the specified number of rows before reading the header or data.
    /// </summary>
    /// <param name="count">The number of rows to skip.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> SkipRows(int count)
    {
        skipRows = count;
        return this;
    }

    /// <summary>
    /// Sets the progress reporter for receiving reading progress updates.
    /// </summary>
    /// <param name="progress">The progress reporter.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithProgress(IProgress<ExcelProgress> progress)
    {
        this.progress = progress;
        return this;
    }

    /// <summary>
    /// Reads all sheets of the same record type and returns results keyed by sheet name.
    /// </summary>
    /// <returns>A builder for same-type multi-sheet reading.</returns>
    public ExcelAllSheetsBuilder<T> AllSheets()
    {
        return new ExcelAllSheetsBuilder<T>(hasHeaderRow, culture, maxRows, skipRows, progress);
    }

    /// <summary>
    /// Reads typed records from an Excel file on disk.
    /// </summary>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <returns>A list of deserialized records.</returns>
    public List<T> FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return FromStream(stream);
    }

    /// <summary>
    /// Reads typed records from a stream containing .xlsx data.
    /// </summary>
    /// <param name="stream">A seekable stream containing .xlsx data.</param>
    /// <returns>A list of deserialized records.</returns>
    public List<T> FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var xlsxReader = new XlsxReader(stream);
        var sheet = ResolveSheet(xlsxReader.Workbook);
        using var sheetReader = xlsxReader.OpenSheet(sheet);

        return ReadRecords(sheetReader, sheet.Name);
    }

    internal List<T> ReadRecords(XlsxSheetReader sheetReader, string currentSheetName)
    {
        // Skip configured rows
        for (int i = 0; i < skipRows; i++)
        {
            if (sheetReader.ReadNextRow() is null)
                return [];
        }

        // Get binder
        var binder = CsvRecordBinderFactory.GetCharBinder<T>(delimiter: '\x01');

        // Read header row if configured
        if (hasHeaderRow)
        {
            var headerCells = sheetReader.ReadNextRow();
            if (headerCells is null)
                return [];

            if (binder.NeedsHeaderResolution)
            {
                var headerBuffer = new char[XlsxRowAdapter.CalculateBufferSize(headerCells) + 1];
                var headerColumnEnds = new int[headerCells.Length + 1];
                var headerRow = XlsxRowAdapter.CreateRow(headerCells, 0, headerBuffer, headerColumnEnds);
                binder.BindHeader(headerRow, 0);
            }
        }

        var results = new List<T>();
        int rowsRead = 0;
        char[] buffer = [];
        int[] columnEnds = [];

        while (true)
        {
            var cells = sheetReader.ReadNextRow();
            if (cells is null)
                break;

            if (maxRows.HasValue && rowsRead >= maxRows.Value)
                break;

            XlsxRowAdapter.EnsureBuffers(cells, ref buffer, ref columnEnds);
            var csvRow = XlsxRowAdapter.CreateRow(cells, sheetReader.CurrentRowNumber, buffer, columnEnds);

            if (binder.TryBind(csvRow, sheetReader.CurrentRowNumber, out var record))
            {
                results.Add(record);
            }

            rowsRead++;

            if (progress is not null && rowsRead % 1000 == 0)
            {
                progress.Report(new ExcelProgress(rowsRead, currentSheetName));
            }
        }

        // Final progress report
        if (progress is not null && rowsRead % 1000 != 0)
        {
            progress.Report(new ExcelProgress(rowsRead, currentSheetName));
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
