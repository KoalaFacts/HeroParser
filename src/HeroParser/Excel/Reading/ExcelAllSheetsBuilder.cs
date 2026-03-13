using System.Globalization;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;

namespace HeroParser.Excels.Reading;

/// <summary>
/// Builder for reading all sheets in an Excel workbook as the same record type.
/// </summary>
/// <typeparam name="T">The record type to deserialize rows into.</typeparam>
public sealed class ExcelAllSheetsBuilder<T> where T : new()
{
    private readonly bool hasHeaderRow;
    private readonly CultureInfo culture;
    private readonly int? maxRows;
    private readonly int skipRows;
    private readonly IProgress<ExcelProgress>? progress;

    internal ExcelAllSheetsBuilder(
        bool hasHeaderRow,
        CultureInfo culture,
        int? maxRows,
        int skipRows,
        IProgress<ExcelProgress>? progress)
    {
        this.hasHeaderRow = hasHeaderRow;
        this.culture = culture;
        this.maxRows = maxRows;
        this.skipRows = skipRows;
        this.progress = progress;
    }

    /// <summary>
    /// Reads all sheets from an Excel file on disk.
    /// </summary>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <returns>A dictionary mapping sheet names to lists of deserialized records.</returns>
    public Dictionary<string, List<T>> FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return FromStream(stream);
    }

    /// <summary>
    /// Reads all sheets from a stream containing .xlsx data.
    /// </summary>
    /// <param name="stream">A seekable stream containing .xlsx data.</param>
    /// <returns>A dictionary mapping sheet names to lists of deserialized records.</returns>
    public Dictionary<string, List<T>> FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var xlsxReader = new XlsxReader(stream);
        var results = new Dictionary<string, List<T>>(StringComparer.Ordinal);

        foreach (var sheet in xlsxReader.Workbook.Sheets)
        {
            using var sheetReader = xlsxReader.OpenSheet(sheet);

            var perSheetBuilder = new ExcelRecordReaderBuilder<T>();
            ConfigureBuilder(perSheetBuilder);

            var records = perSheetBuilder.ReadRecords(sheetReader, sheet.Name);
            results[sheet.Name] = records;
        }

        return results;
    }

    private void ConfigureBuilder(ExcelRecordReaderBuilder<T> builder)
    {
        if (!hasHeaderRow)
            builder.WithoutHeader();
        builder.WithCulture(culture);
        if (maxRows.HasValue)
            builder.WithMaxRows(maxRows.Value);
        builder.SkipRows(skipRows);
        if (progress is not null)
            builder.WithProgress(progress);
    }
}
