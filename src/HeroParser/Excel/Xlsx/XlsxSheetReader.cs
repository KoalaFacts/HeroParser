using System.Globalization;
using System.Xml;

namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Streaming reader for a single Excel worksheet. Reads rows one at a time.
/// </summary>
internal sealed class XlsxSheetReader : IDisposable
{
    private readonly XmlReader reader;
    private readonly XlsxSharedStrings sharedStrings;
    private readonly XlsxStylesheet stylesheet;
    private readonly List<(int ColumnIndex, string Value)> cellBuffer = [];
    private bool inSheetData;
    private bool finished;

    /// <summary>
    /// Creates a new sheet reader for the given worksheet stream.
    /// </summary>
    public XlsxSheetReader(Stream sheetStream, XlsxSharedStrings sharedStrings, XlsxStylesheet stylesheet)
    {
        reader = XmlReader.Create(sheetStream, XlsxXml.CreateReaderSettings());
        this.sharedStrings = sharedStrings;
        this.stylesheet = stylesheet;
    }

    /// <summary>The 1-based Excel row number of the current row.</summary>
    public int CurrentRowNumber { get; private set; }

    /// <summary>Reads the next row. Returns null when no more rows.</summary>
    public string[]? ReadNextRow()
    {
        if (finished)
            return null;

        // Navigate to sheetData if not already there
        if (!inSheetData)
        {
            if (!AdvanceToSheetData())
            {
                finished = true;
                return null;
            }
            inSheetData = true;
        }

        // Find the next <row> element
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "sheetData")
            {
                finished = true;
                return null;
            }

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
            {
                var rowNumStr = reader.GetAttribute("r");
                if (rowNumStr is not null && int.TryParse(rowNumStr, out int rowNum))
                    CurrentRowNumber = rowNum;

                return ReadRowCells();
            }
        }

        finished = true;
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        reader.Dispose();
    }

    private bool AdvanceToSheetData()
    {
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheetData")
            {
                return !reader.IsEmptyElement;
            }
        }
        return false;
    }

    private string[] ReadRowCells()
    {
        if (reader.IsEmptyElement)
            return [];

        cellBuffer.Clear();
        int maxColumnIndex = -1;

        // Use ReadSubtree to safely iterate cells within this row
        using var rowReader = reader.ReadSubtree();
        rowReader.Read(); // move to the <row> element itself

        while (rowReader.Read())
        {
            if (rowReader.NodeType == XmlNodeType.Element && rowReader.LocalName == "c")
            {
                var cellRef = rowReader.GetAttribute("r");
                var typeAttr = rowReader.GetAttribute("t");
                var styleAttr = rowReader.GetAttribute("s");

                int columnIndex = cellRef is not null ? ParseColumnIndex(cellRef) : cellBuffer.Count;
                var cellType = ParseCellType(typeAttr);
                int styleIndex = styleAttr is not null && int.TryParse(styleAttr, out int si) ? si : -1;

                var value = ReadCellValue(rowReader, cellType, styleIndex);

                cellBuffer.Add((columnIndex, value));
                if (columnIndex > maxColumnIndex)
                    maxColumnIndex = columnIndex;
            }
        }

        if (cellBuffer.Count == 0)
            return [];

        // Build the result array, filling sparse gaps with empty strings
        var result = new string[maxColumnIndex + 1];
        Array.Fill(result, "");

        foreach (var (columnIndex, value) in cellBuffer)
        {
            result[columnIndex] = value;
        }

        return result;
    }

    private string ReadCellValue(XmlReader cellReader, XlsxCellType cellType, int styleIndex)
    {
        if (cellReader.IsEmptyElement)
            return "";

        string? rawValue = null;
        string? inlineText = null;

        // Use ReadSubtree to safely read within this cell
        using var subtree = cellReader.ReadSubtree();
        subtree.Read(); // move to the <c> element itself

        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
                continue;

            if (subtree.LocalName == "v")
            {
                rawValue = subtree.IsEmptyElement ? "" : subtree.ReadElementContentAsString();
            }
            else if (subtree.LocalName == "is")
            {
                inlineText = ReadInlineString(subtree);
            }
        }

        return cellType switch
        {
            XlsxCellType.SharedString => ResolveSharedString(rawValue),
            XlsxCellType.InlineString => inlineText ?? "",
            XlsxCellType.Boolean => rawValue == "1" ? "TRUE" : "FALSE",
            XlsxCellType.Error => "",
            XlsxCellType.String => rawValue ?? "",
            XlsxCellType.Number => ConvertNumber(rawValue, styleIndex),
            _ => rawValue ?? ""
        };
    }

    private static string ReadInlineString(XmlReader isReader)
    {
        // <is><t>text</t></is>
        if (isReader.IsEmptyElement)
            return "";

        using var subtree = isReader.ReadSubtree();
        subtree.Read(); // move to <is>

        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element && subtree.LocalName == "t")
            {
                return subtree.IsEmptyElement ? "" : subtree.ReadElementContentAsString();
            }
        }

        return "";
    }

    private string ResolveSharedString(string? rawValue)
    {
        if (rawValue is null)
            return "";

        if (int.TryParse(rawValue, out int index) && index >= 0 && index < sharedStrings.Count)
            return sharedStrings[index];

        return "";
    }

    private string ConvertNumber(string? rawValue, int styleIndex)
    {
        if (rawValue is null)
            return "";

        // Check if this is a date-formatted cell
        if (styleIndex >= 0 && stylesheet.IsDateFormat(styleIndex))
        {
            if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double oleDate))
            {
                return ConvertOleDate(oleDate);
            }
        }

        return rawValue;
    }

    private static string ConvertOleDate(double oleDate)
    {
        // Time-only values (< 1.0) are converted to TimeSpan string
        if (oleDate < 1.0 && oleDate >= 0.0)
        {
            var timeSpan = TimeSpan.FromDays(oleDate);
            return timeSpan.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        var dateTime = DateTime.FromOADate(oleDate);
        return dateTime.ToString("s", CultureInfo.InvariantCulture);
    }

    private static XlsxCellType ParseCellType(string? typeAttr)
    {
        return typeAttr switch
        {
            "s" => XlsxCellType.SharedString,
            "inlineStr" => XlsxCellType.InlineString,
            "b" => XlsxCellType.Boolean,
            "e" => XlsxCellType.Error,
            "str" => XlsxCellType.String,
            _ => XlsxCellType.Number // default (no t attr or t="n")
        };
    }

    /// <summary>
    /// Parses the column index from a cell reference like "A1", "B3", "AA26".
    /// Returns 0-based column index (A=0, B=1, ..., Z=25, AA=26, etc.).
    /// </summary>
    internal static int ParseColumnIndex(string cellRef)
    {
        int columnIndex = 0;
        int i = 0;

        while (i < cellRef.Length && char.IsLetter(cellRef[i]))
        {
            columnIndex = columnIndex * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1);
            i++;
        }

        return columnIndex - 1; // Convert from 1-based to 0-based
    }
}
