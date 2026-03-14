using System.IO.Compression;
using System.Text;

namespace HeroParser.Tests.Fixtures.Excel;

/// <summary>
/// Creates minimal .xlsx files in memory for testing.
/// </summary>
internal static class ExcelTestHelper
{
    /// <summary>Creates a single-sheet .xlsx with the given rows (first row = headers if applicable).</summary>
    public static MemoryStream CreateXlsx(string sheetName, string[][] rows)
    {
        return CreateXlsx(new Dictionary<string, string[][]> { [sheetName] = rows });
    }

    /// <summary>Creates a multi-sheet .xlsx.</summary>
    public static MemoryStream CreateXlsx(Dictionary<string, string[][]> sheets)
    {
        var ms = new MemoryStream();

        // Collect all unique strings across all sheets for the shared string table
        var uniqueStrings = new Dictionary<string, int>(StringComparer.Ordinal);
        var stringList = new List<string>();

        foreach (var (_, rows) in sheets)
        {
            foreach (var row in rows)
            {
                foreach (var cell in row)
                {
                    if (!IsNumeric(cell) && !uniqueStrings.ContainsKey(cell))
                    {
                        uniqueStrings[cell] = stringList.Count;
                        stringList.Add(cell);
                    }
                }
            }
        }

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetNames = sheets.Keys.ToList();

            // [Content_Types].xml
            WriteContentTypes(zip, sheetNames.Count);

            // _rels/.rels
            WriteRootRels(zip);

            // xl/workbook.xml
            WriteWorkbook(zip, sheetNames);

            // xl/_rels/workbook.xml.rels
            WriteWorkbookRels(zip, sheetNames.Count);

            // xl/sharedStrings.xml
            WriteSharedStrings(zip, stringList);

            // xl/styles.xml (minimal)
            WriteStyles(zip, []);

            // xl/worksheets/sheetN.xml
            int sheetIndex = 1;
            foreach (var (_, rows) in sheets)
            {
                WriteSheet(zip, sheetIndex, rows, uniqueStrings);
                sheetIndex++;
            }
        }

        ms.Position = 0;
        return ms;
    }

    /// <summary>Creates .xlsx with date-formatted cells.</summary>
    public static MemoryStream CreateXlsxWithDates(
        string sheetName,
        string[] headers,
        (double oleDate, int styleId)[][] dataRows)
    {
        var ms = new MemoryStream();

        // Collect unique strings from headers
        var uniqueStrings = new Dictionary<string, int>(StringComparer.Ordinal);
        var stringList = new List<string>();
        foreach (var header in headers)
        {
            if (!uniqueStrings.ContainsKey(header))
            {
                uniqueStrings[header] = stringList.Count;
                stringList.Add(header);
            }
        }

        // Create date format entry: numFmtId=164, formatCode="yyyy-mm-dd"
        var dateFormats = new List<(int NumFmtId, string FormatCode)>
        {
            (164, "yyyy-mm-dd")
        };

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteContentTypes(zip, 1);
            WriteRootRels(zip);
            WriteWorkbook(zip, [sheetName]);
            WriteWorkbookRels(zip, 1);
            WriteSharedStrings(zip, stringList);
            WriteStyles(zip, dateFormats);
            WriteDateSheet(zip, 1, headers, dataRows, uniqueStrings);
        }

        ms.Position = 0;
        return ms;
    }

    private static void WriteContentTypes(ZipArchive zip, int sheetCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine("""<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""");
        sb.AppendLine("""  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""");
        sb.AppendLine("""  <Default Extension="xml" ContentType="application/xml"/>""");
        sb.AppendLine("""  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""");
        sb.AppendLine("""  <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>""");
        sb.AppendLine("""  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        for (int i = 1; i <= sheetCount; i++)
        {
            sb.AppendLine($"""  <Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        }
        sb.AppendLine("</Types>");
        AddEntry(zip, "[Content_Types].xml", sb.ToString());
    }

    private static void WriteRootRels(ZipArchive zip)
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """;
        AddEntry(zip, "_rels/.rels", xml);
    }

    private static void WriteWorkbook(ZipArchive zip, List<string> sheetNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine("""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""");
        sb.AppendLine("  <sheets>");
        for (int i = 0; i < sheetNames.Count; i++)
        {
            sb.AppendLine($"""    <sheet name="{EscapeXml(sheetNames[i])}" sheetId="{i + 1}" r:id="rId{i + 1}"/>""");
        }
        sb.AppendLine("  </sheets>");
        sb.AppendLine("</workbook>");
        AddEntry(zip, "xl/workbook.xml", sb.ToString());
    }

    private static void WriteWorkbookRels(ZipArchive zip, int sheetCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine("""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        for (int i = 1; i <= sheetCount; i++)
        {
            sb.AppendLine($"""  <Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
        }
        sb.AppendLine($"""  <Relationship Id="rId{sheetCount + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>""");
        sb.AppendLine($"""  <Relationship Id="rId{sheetCount + 2}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>""");
        sb.AppendLine("</Relationships>");
        AddEntry(zip, "xl/_rels/workbook.xml.rels", sb.ToString());
    }

    private static void WriteSharedStrings(ZipArchive zip, List<string> strings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine($"""<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{strings.Count}" uniqueCount="{strings.Count}">""");
        foreach (var s in strings)
        {
            sb.AppendLine($"  <si><t>{EscapeXml(s)}</t></si>");
        }
        sb.AppendLine("</sst>");
        AddEntry(zip, "xl/sharedStrings.xml", sb.ToString());
    }

    private static void WriteStyles(ZipArchive zip, List<(int NumFmtId, string FormatCode)> customFormats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine("""<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");

        if (customFormats.Count > 0)
        {
            sb.AppendLine($"""  <numFmts count="{customFormats.Count}">""");
            foreach (var (numFmtId, formatCode) in customFormats)
            {
                sb.AppendLine($"""    <numFmt numFmtId="{numFmtId}" formatCode="{EscapeXml(formatCode)}"/>""");
            }
            sb.AppendLine("  </numFmts>");
        }

        // Cell formats: index 0 = General, index 1 = date format (if any)
        if (customFormats.Count > 0)
        {
            sb.AppendLine("""  <cellXfs count="2">""");
            sb.AppendLine("""    <xf numFmtId="0"/>""");
            sb.AppendLine($"""    <xf numFmtId="{customFormats[0].NumFmtId}"/>""");
            sb.AppendLine("  </cellXfs>");
        }
        else
        {
            sb.AppendLine("""  <cellXfs count="1">""");
            sb.AppendLine("""    <xf numFmtId="0"/>""");
            sb.AppendLine("  </cellXfs>");
        }

        sb.AppendLine("</styleSheet>");
        AddEntry(zip, "xl/styles.xml", sb.ToString());
    }

    private static void WriteSheet(
        ZipArchive zip,
        int sheetIndex,
        string[][] rows,
        Dictionary<string, int> sharedStrings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.AppendLine("  <sheetData>");

        for (int r = 0; r < rows.Length; r++)
        {
            int rowNum = r + 1;
            sb.AppendLine($"""    <row r="{rowNum}">""");

            for (int c = 0; c < rows[r].Length; c++)
            {
                var cellRef = GetCellRef(c, rowNum);
                var value = rows[r][c];

                if (IsNumeric(value))
                {
                    // Numeric cell (no t attribute)
                    sb.AppendLine($"""      <c r="{cellRef}"><v>{value}</v></c>""");
                }
                else
                {
                    // Shared string reference
                    var ssIndex = sharedStrings[value];
                    sb.AppendLine($"""      <c r="{cellRef}" t="s"><v>{ssIndex}</v></c>""");
                }
            }

            sb.AppendLine("    </row>");
        }

        sb.AppendLine("  </sheetData>");
        sb.AppendLine("</worksheet>");
        AddEntry(zip, $"xl/worksheets/sheet{sheetIndex}.xml", sb.ToString());
    }

    private static void WriteDateSheet(
        ZipArchive zip,
        int sheetIndex,
        string[] headers,
        (double oleDate, int styleId)[][] dataRows,
        Dictionary<string, int> sharedStrings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.AppendLine("  <sheetData>");

        // Header row
        sb.AppendLine("""    <row r="1">""");
        for (int c = 0; c < headers.Length; c++)
        {
            var cellRef = GetCellRef(c, 1);
            var ssIndex = sharedStrings[headers[c]];
            sb.AppendLine($"""      <c r="{cellRef}" t="s"><v>{ssIndex}</v></c>""");
        }
        sb.AppendLine("    </row>");

        // Data rows with date values
        for (int r = 0; r < dataRows.Length; r++)
        {
            int rowNum = r + 2; // 1-based, after header
            sb.AppendLine($"""    <row r="{rowNum}">""");

            for (int c = 0; c < dataRows[r].Length; c++)
            {
                var cellRef = GetCellRef(c, rowNum);
                var (oleDate, styleId) = dataRows[r][c];
                sb.AppendLine($"""      <c r="{cellRef}" s="{styleId}"><v>{oleDate.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>""");
            }

            sb.AppendLine("    </row>");
        }

        sb.AppendLine("  </sheetData>");
        sb.AppendLine("</worksheet>");
        AddEntry(zip, $"xl/worksheets/sheet{sheetIndex}.xml", sb.ToString());
    }

    private static string GetCellRef(int columnIndex, int rowNumber)
    {
        var column = GetColumnName(columnIndex);
        return $"{column}{rowNumber}";
    }

    private static string GetColumnName(int columnIndex)
    {
        var result = new StringBuilder();
        int index = columnIndex;

        do
        {
            result.Insert(0, (char)('A' + index % 26));
            index = index / 26 - 1;
        } while (index >= 0);

        return result.ToString();
    }

    private static bool IsNumeric(string value)
    {
        return double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static void AddEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
