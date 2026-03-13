using System.IO.Compression;
using System.Xml;
using HeroParser.Excels.Core;

namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Parses workbook metadata (sheet names, indices, paths) from an .xlsx file.
/// </summary>
internal sealed class XlsxWorkbook
{
    /// <summary>Information about a sheet in the workbook.</summary>
    public record SheetInfo(string Name, int Index, string Path);

    private readonly SheetInfo[] sheets;

    private XlsxWorkbook(SheetInfo[] sheets)
    {
        this.sheets = sheets;
    }

    /// <summary>All sheets in order.</summary>
    public IReadOnlyList<SheetInfo> Sheets => sheets;

    /// <summary>Gets a sheet by name.</summary>
    /// <exception cref="ExcelException">Thrown if no sheet with the given name exists.</exception>
    public SheetInfo GetSheetByName(string name)
    {
        for (int i = 0; i < sheets.Length; i++)
        {
            if (string.Equals(sheets[i].Name, name, StringComparison.Ordinal))
                return sheets[i];
        }

        throw new ExcelException($"Sheet '{name}' not found in workbook.");
    }

    /// <summary>Gets a sheet by zero-based index.</summary>
    /// <exception cref="ExcelException">Thrown if the index is out of range.</exception>
    public SheetInfo GetSheetByIndex(int index)
    {
        if ((uint)index >= (uint)sheets.Length)
            throw new ExcelException($"Sheet index {index} is out of range. Workbook contains {sheets.Length} sheet(s).");

        return sheets[index];
    }

    /// <summary>Gets the first sheet in the workbook.</summary>
    /// <exception cref="ExcelException">Thrown if the workbook has no sheets.</exception>
    public SheetInfo GetFirstSheet()
    {
        if (sheets.Length == 0)
            throw new ExcelException("Workbook contains no sheets.");

        return sheets[0];
    }

    /// <summary>
    /// Parses workbook.xml and workbook.xml.rels from the given ZIP archive.
    /// </summary>
    public static XlsxWorkbook Parse(ZipArchive archive)
    {
        // Step 1: Parse relationships to map rId -> target path
        var relationships = ParseRelationships(archive);

        // Step 2: Parse workbook.xml to get sheet definitions
        var sheetEntries = ParseWorkbookSheets(archive);

        // Step 3: Combine sheet entries with resolved paths
        var sheets = new SheetInfo[sheetEntries.Count];
        for (int i = 0; i < sheetEntries.Count; i++)
        {
            var (name, rId) = sheetEntries[i];
            var path = relationships.TryGetValue(rId, out var target)
                ? NormalizePath(target)
                : throw new ExcelException($"Relationship '{rId}' for sheet '{name}' not found.");

            sheets[i] = new SheetInfo(name, i, path);
        }

        return new XlsxWorkbook(sheets);
    }

    private static Dictionary<string, string> ParseRelationships(ZipArchive archive)
    {
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry is null)
            return [];

        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        var settings = new XmlReaderSettings { IgnoreWhitespace = true };

        using var stream = relsEntry.Open();
        using var reader = XmlReader.Create(stream, settings);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship")
            {
                var id = reader.GetAttribute("Id");
                var target = reader.GetAttribute("Target");
                if (id is not null && target is not null)
                    relationships[id] = target;
            }
        }

        return relationships;
    }

    private static List<(string Name, string RId)> ParseWorkbookSheets(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new ExcelException("Missing xl/workbook.xml in .xlsx archive.");

        var settings = new XmlReaderSettings { IgnoreWhitespace = true };
        var sheets = new List<(string Name, string RId)>();

        using var stream = workbookEntry.Open();
        using var reader = XmlReader.Create(stream, settings);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
            {
                var name = reader.GetAttribute("name");
                // r:id attribute — namespace-aware lookup
                var rId = reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");

                if (name is not null && rId is not null)
                    sheets.Add((name, rId));
            }
        }

        return sheets;
    }

    private static string NormalizePath(string target)
    {
        // Targets in rels are relative to the xl/ directory
        // e.g., "worksheets/sheet1.xml" → "xl/worksheets/sheet1.xml"
        if (target.StartsWith('/'))
            return target[1..]; // absolute path from package root

        return "xl/" + target;
    }
}
