using System.IO.Compression;
using System.Xml;
using HeroParser.Excels.Core;

namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Parses workbook metadata (sheet names, indices, paths) from an .xlsx file.
/// </summary>
internal sealed class XlsxWorkbook
{
    private const string WORKSHEET_REL_TYPE_TRANSITIONAL = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private const string WORKSHEET_REL_TYPE_STRICT = "http://purl.oclc.org/ooxml/officeDocument/relationships/worksheet";

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

        // Step 3: Filter to worksheets and combine with resolved paths while preserving workbook order
        var sheets = new List<SheetInfo>(sheetEntries.Count);
        for (int i = 0; i < sheetEntries.Count; i++)
        {
            var (name, rId) = sheetEntries[i];
            if (!relationships.TryGetValue(rId, out var rel))
                throw new ExcelException($"Relationship '{rId}' for sheet '{name}' not found.");

            if (!IsWorksheetRelationshipType(rel.Type))
                continue;

            var path = NormalizePath(rel.Target);
            sheets.Add(new SheetInfo(name, sheets.Count, path));
        }

        return new XlsxWorkbook([.. sheets]);
    }

    private static bool IsWorksheetRelationshipType(string? relationshipType)
        => string.Equals(relationshipType, WORKSHEET_REL_TYPE_TRANSITIONAL, StringComparison.Ordinal)
            || string.Equals(relationshipType, WORKSHEET_REL_TYPE_STRICT, StringComparison.Ordinal);

    private static Dictionary<string, (string Target, string? Type)> ParseRelationships(ZipArchive archive)
    {
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry is null)
            return [];

        new XlsxXml().ValidateEntrySize(relsEntry);
        var relationships = new Dictionary<string, (string Target, string? Type)>(StringComparer.Ordinal);

        using var stream = relsEntry.Open();
        using var reader = XmlReader.Create(stream, new XlsxXml().CreateReaderSettings());

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship")
            {
                var id = reader.GetAttribute("Id");
                var target = reader.GetAttribute("Target");
                var type = reader.GetAttribute("Type");
                if (id is not null && target is not null)
                    relationships[id] = (target, type);
            }
        }

        return relationships;
    }

    private static List<(string Name, string RId)> ParseWorkbookSheets(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new ExcelException("Missing xl/workbook.xml in .xlsx archive.");

        new XlsxXml().ValidateEntrySize(workbookEntry);
        var sheets = new List<(string Name, string RId)>();

        using var stream = workbookEntry.Open();
        using var reader = XmlReader.Create(stream, new XlsxXml().CreateReaderSettings());

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
            {
                var name = reader.GetAttribute("name");
                // r:id attribute — namespace-aware lookup supporting both Transitional and Strict OpenXML namespaces
                var rId = reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
                    ?? reader.GetAttribute("id", "http://purl.oclc.org/ooxml/officeDocument/relationships");

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
