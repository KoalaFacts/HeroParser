using System.IO.Compression;
using HeroParser.Excels.Core;

namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Orchestrates reading of .xlsx files. Opens the ZIP archive, parses metadata,
/// and provides access to individual sheet readers.
/// </summary>
internal sealed class XlsxReader : IDisposable
{
    // Zip-bomb mitigation: reject entries whose decompressed-to-compressed ratio exceeds this
    // threshold AND whose declared uncompressed size is non-trivial. XLSX shared-strings and
    // sheets routinely hit 10-30x compression on repeated text, so the cap is generous.
    private const long MAX_ENTRY_DECOMPRESSED_BYTES = 512L * 1024 * 1024; // 512 MB
    private const int MAX_COMPRESSION_RATIO = 200;
    private const long RATIO_CHECK_MIN_COMPRESSED_BYTES = 1024;

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
        catch (ExcelException)
        {
            archive?.Dispose();
            throw;
        }
        catch (Exception ex)
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

        ValidateEntrySize(entry);
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

        ValidateEntrySize(entry);
        using var stream = entry.Open();
        return XlsxSharedStrings.Parse(stream);
    }

    private static XlsxStylesheet ParseStylesheet(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/styles.xml");
        if (entry is null)
            return XlsxStylesheet.Parse(null);

        ValidateEntrySize(entry);
        using var stream = entry.Open();
        return XlsxStylesheet.Parse(stream);
    }

    private static void ValidateEntrySize(ZipArchiveEntry entry)
    {
        if (entry.Length > MAX_ENTRY_DECOMPRESSED_BYTES)
        {
            throw new ExcelException(
                $"Refusing to open .xlsx entry '{entry.FullName}': declared uncompressed size " +
                $"{entry.Length} exceeds limit of {MAX_ENTRY_DECOMPRESSED_BYTES} bytes (possible zip bomb).");
        }

        if (entry.CompressedLength >= RATIO_CHECK_MIN_COMPRESSED_BYTES
            && entry.Length / entry.CompressedLength > MAX_COMPRESSION_RATIO)
        {
            throw new ExcelException(
                $"Refusing to open .xlsx entry '{entry.FullName}': compression ratio " +
                $"{entry.Length}/{entry.CompressedLength} exceeds {MAX_COMPRESSION_RATIO}:1 (possible zip bomb).");
        }
    }
}
