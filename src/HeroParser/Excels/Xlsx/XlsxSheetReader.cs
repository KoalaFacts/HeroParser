using System.Globalization;
using System.Xml;
using HeroParser.Excels.Core;

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

        try
        {
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
                    else
                        CurrentRowNumber++;

                    return ReadRowCells();
                }
            }

            finished = true;
            return null;
        }
        catch (XmlException ex)
        {
            throw new ExcelException("Failed to read Excel worksheet due to XML corruption.", ex);
        }
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
        int rowDepth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == rowDepth && reader.LocalName == "row")
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c")
            {
                var cellRef = reader.GetAttribute("r");
                var typeAttr = reader.GetAttribute("t");
                var styleAttr = reader.GetAttribute("s");

                int columnIndex = cellRef is not null ? ParseColumnIndex(cellRef) : cellBuffer.Count;
                if (columnIndex < 0)
                {
                    throw new ExcelException($"Invalid column index '{columnIndex}' in cell reference '{cellRef}'.");
                }
                var cellType = ParseCellType(typeAttr);
                int styleIndex = styleAttr is not null && int.TryParse(styleAttr, out int si) ? si : -1;

                var value = ReadCellValue(cellType, styleIndex);

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

    private string ReadCellValue(XlsxCellType cellType, int styleIndex)
    {
        if (reader.IsEmptyElement)
            return "";

        string? rawValue = null;
        string? inlineText = null;
        int cellDepth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == cellDepth && reader.LocalName == "c")
                break;

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.LocalName == "v")
            {
                rawValue = "";
                if (!reader.IsEmptyElement)
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "v")
                            break;
                        if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.SignificantWhitespace)
                        {
                            rawValue += reader.Value;
                        }
                    }
                }
            }
            else if (reader.LocalName == "is")
            {
                inlineText = ReadInlineString();
            }
        }

        return cellType switch
        {
            XlsxCellType.SharedString => ResolveSharedString(rawValue),
            XlsxCellType.InlineString => inlineText ?? "",
            XlsxCellType.Boolean => rawValue is null ? "" : (rawValue == "1" || string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE"),
            XlsxCellType.Error => "",
            XlsxCellType.String => rawValue ?? "",
            XlsxCellType.Number => ConvertNumber(rawValue, styleIndex),
            _ => rawValue ?? ""
        };
    }

    private string ReadInlineString()
    {
        // <is><t>text</t></is>
        if (reader.IsEmptyElement)
            return "";

        int isDepth = reader.Depth;
        string result = "";

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == isDepth && reader.LocalName == "is")
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
            {
                if (!reader.IsEmptyElement)
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "t")
                            break;
                        if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.SignificantWhitespace)
                        {
                            result += reader.Value;
                        }
                    }
                }
            }
        }

        return result;
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
        if (double.IsNaN(oleDate) || double.IsInfinity(oleDate) || oleDate < -657435.0 || oleDate >= 2958466.0)
        {
            // OLE Automation Date limits: -657435.0 (Jan 1, 0100) to 2958465.9999884 (Dec 31, 9999)
            // Degrade gracefully by returning the raw float string in invariant culture
            return oleDate.ToString(CultureInfo.InvariantCulture);
        }

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
        // Excel's maximum column is XFD (1-based 16384 / 0-based 16383). Cap the loop
        // so an attacker-crafted cellRef like "ZZZZZZZZZZZZ1" can't overflow the int
        // multiplication and bypass downstream bounds checks.
        const int MAX_ONE_BASED_COLUMN = 16384;

        int columnIndex = 0;
        int i = 0;

        while (i < cellRef.Length && char.IsLetter(cellRef[i]))
        {
            columnIndex = columnIndex * 26 + (char.ToUpperInvariant(cellRef[i]) - 'A' + 1);
            if (columnIndex > MAX_ONE_BASED_COLUMN)
            {
                throw new ExcelException(
                    $"Cell reference '{cellRef}' exceeds Excel's maximum column 'XFD' ({MAX_ONE_BASED_COLUMN}).");
            }
            i++;
        }

        if (i == 0)
        {
            throw new ExcelException($"Cell reference '{cellRef}' is invalid: it does not start with any letters.");
        }

        return columnIndex - 1; // Convert from 1-based to 0-based
    }
}
