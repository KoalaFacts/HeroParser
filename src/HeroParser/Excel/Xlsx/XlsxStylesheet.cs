using System.Xml;

namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Parses the styles (xl/styles.xml) from an .xlsx file to detect date-formatted cells.
/// </summary>
internal sealed class XlsxStylesheet
{
    // Built-in Excel number format IDs that represent date/time formats.
    // IDs 14-22 are always date/time per the ECMA-376 specification.
    private static readonly HashSet<int> builtInDateFormatIds =
    [
        14, // m/d/yyyy
        15, // d-mmm-yy
        16, // d-mmm
        17, // mmm-yy
        18, // h:mm AM/PM
        19, // h:mm:ss AM/PM
        20, // h:mm
        21, // h:mm:ss
        22  // m/d/yyyy h:mm
    ];

    private readonly bool[] dateStyleFlags;

    private XlsxStylesheet(bool[] dateStyleFlags)
    {
        this.dateStyleFlags = dateStyleFlags;
    }

    /// <summary>
    /// Determines whether the cell style at the given index is a date/time format.
    /// </summary>
    public bool IsDateFormat(int styleIndex)
    {
        if ((uint)styleIndex >= (uint)dateStyleFlags.Length)
            return false;

        return dateStyleFlags[styleIndex];
    }

    /// <summary>
    /// Parses the stylesheet from the given stream. Returns an empty stylesheet if stream is null.
    /// </summary>
    public static XlsxStylesheet Parse(Stream? stream)
    {
        if (stream is null)
            return new XlsxStylesheet([]);

        var settings = new XmlReaderSettings { IgnoreWhitespace = true };
        using var reader = XmlReader.Create(stream, settings);

        // Step 1: Parse custom number formats from <numFmts>
        var customFormats = new Dictionary<int, string>();
        // Step 2: Parse cell format entries from <cellXfs>
        var cellXfNumFmtIds = new List<int>();

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.LocalName == "numFmts")
            {
                ParseNumFmts(reader, customFormats);
            }
            else if (reader.LocalName == "cellXfs")
            {
                ParseCellXfs(reader, cellXfNumFmtIds);
            }
        }

        // Step 3: Build the date style flags array
        var flags = new bool[cellXfNumFmtIds.Count];
        for (int i = 0; i < cellXfNumFmtIds.Count; i++)
        {
            int numFmtId = cellXfNumFmtIds[i];
            flags[i] = IsDateNumFmtId(numFmtId, customFormats);
        }

        return new XlsxStylesheet(flags);
    }

    private static void ParseNumFmts(XmlReader reader, Dictionary<int, string> customFormats)
    {
        if (reader.IsEmptyElement)
            return;

        int depth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "numFmt")
            {
                var idStr = reader.GetAttribute("numFmtId");
                var formatCode = reader.GetAttribute("formatCode");
                if (idStr is not null && formatCode is not null && int.TryParse(idStr, out int id))
                {
                    customFormats[id] = formatCode;
                }
            }
        }
    }

    private static void ParseCellXfs(XmlReader reader, List<int> cellXfNumFmtIds)
    {
        if (reader.IsEmptyElement)
            return;

        int depth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "xf")
            {
                var numFmtIdStr = reader.GetAttribute("numFmtId");
                int numFmtId = 0;
                if (numFmtIdStr is not null)
                    int.TryParse(numFmtIdStr, out numFmtId);

                cellXfNumFmtIds.Add(numFmtId);
            }
        }
    }

    private static bool IsDateNumFmtId(int numFmtId, Dictionary<int, string> customFormats)
    {
        // Check built-in date format IDs
        if (builtInDateFormatIds.Contains(numFmtId))
            return true;

        // Check custom formats
        if (customFormats.TryGetValue(numFmtId, out var formatCode))
            return IsDateFormatCode(formatCode);

        return false;
    }

    private static bool IsDateFormatCode(string formatCode)
    {
        // A format code is a date/time format if it contains date/time characters
        // (y, m, d, h, s) outside of literal strings (enclosed in quotes or preceded by backslash).
        // We need to exclude pure number formats like "0.00" or "#,##0".

        bool hasDateChars = false;
        bool inLiteral = false;

        for (int i = 0; i < formatCode.Length; i++)
        {
            char c = formatCode[i];

            // Handle quoted strings
            if (c == '"')
            {
                inLiteral = !inLiteral;
                continue;
            }

            if (inLiteral)
                continue;

            // Handle escaped characters
            if (c == '\\' && i + 1 < formatCode.Length)
            {
                i++; // skip next character
                continue;
            }

            // Check for date/time format characters (case-insensitive)
            char lower = char.ToLowerInvariant(c);
            if (lower is 'y' or 'd' or 'h' or 's')
            {
                hasDateChars = true;
            }
            else if (lower == 'm')
            {
                // 'm' is ambiguous: it can be month or minutes.
                // In date context, it's month. In time context (after h or before s), it's minutes.
                // Either way, it indicates a date/time format.
                hasDateChars = true;
            }
        }

        return hasDateChars;
    }
}
