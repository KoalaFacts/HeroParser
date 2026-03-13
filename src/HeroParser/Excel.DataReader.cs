using HeroParser.Excels.Reading.Data;
using HeroParser.Excels.Xlsx;

namespace HeroParser;

public static partial class Excel
{
    /// <summary>
    /// Creates an <see cref="System.Data.IDataReader"/> over an Excel .xlsx stream.
    /// </summary>
    /// <param name="stream">A seekable stream containing .xlsx data.</param>
    /// <param name="sheetName">Optional sheet name. When null, the first sheet is read.</param>
    /// <param name="hasHeaderRow">Whether the first row contains column headers. Default: true.</param>
    /// <param name="skipRows">Number of rows to skip before reading headers/data. Default: 0.</param>
    /// <returns>A data reader that exposes all cell values as strings.</returns>
    public static ExcelDataReader CreateDataReader(
        Stream stream,
        string? sheetName = null,
        bool hasHeaderRow = true,
        int skipRows = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var xlsxReader = new XlsxReader(stream);
        try
        {
            var sheet = sheetName is not null
                ? xlsxReader.Workbook.GetSheetByName(sheetName)
                : xlsxReader.Workbook.GetFirstSheet();
            var sheetReader = xlsxReader.OpenSheet(sheet);

            return new ExcelDataReader(xlsxReader, sheetReader, hasHeaderRow, skipRows);
        }
        catch
        {
            xlsxReader.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates an <see cref="System.Data.IDataReader"/> over an Excel .xlsx file on disk.
    /// </summary>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <param name="sheetName">Optional sheet name. When null, the first sheet is read.</param>
    /// <param name="hasHeaderRow">Whether the first row contains column headers. Default: true.</param>
    /// <param name="skipRows">Number of rows to skip before reading headers/data. Default: 0.</param>
    /// <returns>A data reader that exposes all cell values as strings.</returns>
    public static ExcelDataReader CreateDataReader(
        string path,
        string? sheetName = null,
        bool hasHeaderRow = true,
        int skipRows = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return CreateDataReader(stream, sheetName, hasHeaderRow, skipRows);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
