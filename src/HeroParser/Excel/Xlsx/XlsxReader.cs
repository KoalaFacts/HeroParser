using System.IO.Compression;
using HeroParser.Excel.Core;

namespace HeroParser.Excel.Xlsx;

/// <summary>
/// Orchestrates reading of .xlsx files. Opens the ZIP archive, parses metadata,
/// and provides access to individual sheet readers.
/// </summary>
internal sealed class XlsxReader : IDisposable
{
    private readonly ZipArchive archive;
    private readonly XlsxSharedStrings sharedStrings;
    private readonly XlsxStylesheet stylesheet;

    /// <summary>
    /// Opens an .xlsx file from the given stream.
    /// </summary>
    /// <param name="stream">A seekable stream containing the .xlsx file data.</param>
    public XlsxReader(Stream stream)
    {
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            Workbook = XlsxWorkbook.Parse(archive);
            sharedStrings = ParseSharedStrings(archive);
            stylesheet = ParseStylesheet(archive);
        }
        catch (Exception ex) when (ex is not ExcelException)
        {
            archive?.Dispose();
            throw new ExcelException("Failed to open .xlsx file: " + ex.Message, ex);
        }
    }

    /// <summary>The parsed workbook metadata (sheet names and paths).</summary>
    public XlsxWorkbook Workbook { get; }

    /// <summary>
    /// Opens a sheet reader for the specified sheet.
    /// </summary>
    public XlsxSheetReader OpenSheet(XlsxWorkbook.SheetInfo sheet)
    {
        var entry = archive.GetEntry(sheet.Path)
            ?? throw new ExcelException($"Sheet file '{sheet.Path}' not found in .xlsx archive.");

        var stream = entry.Open();
        return new XlsxSheetReader(stream, sharedStrings, stylesheet);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        archive.Dispose();
    }

    private static XlsxSharedStrings ParseSharedStrings(ZipArchive archive)
    {
        // sharedStrings.xml may not exist (workbooks with only numbers/formulas)
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return XlsxSharedStrings.Empty;

        using var stream = entry.Open();
        return XlsxSharedStrings.Parse(stream);
    }

    private static XlsxStylesheet ParseStylesheet(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/styles.xml");
        if (entry is null)
            return XlsxStylesheet.Parse(null);

        using var stream = entry.Open();
        return XlsxStylesheet.Parse(stream);
    }
}
