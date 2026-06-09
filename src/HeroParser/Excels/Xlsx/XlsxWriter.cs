using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Xml;
using HeroParser.Excels.Core;

namespace HeroParser.Excels.Xlsx;

// Creates a valid .xlsx ZIP package by writing XML parts using XmlWriter.
// Usage:
//   using var writer = new XlsxWriter(stream);
//   var sheet = writer.StartSheet("Sheet1");
//   sheet.WriteHeaderRow(["Name", "Age"]);
//   sheet.StartRow(2);
//   sheet.WriteCellString(1, "Alice");
//   sheet.WriteCellNumber(2, 30);
//   sheet.EndRow();
//   sheet.Close();
//   // Dispose writes the workbook-level XML parts.
/// <summary>
/// Low-level writer for creating .xlsx packages. Manages ZIP archive entries and XML generation.
/// </summary>
/// <remarks>
/// This type is public to support source-generated direct writers that avoid boxing.
/// Prefer the higher-level <c>Excel.Write&lt;T&gt;()</c> API for general use.
/// </remarks>
public sealed class XlsxWriter : IDisposable
{
    // numFmtId 14 = "m/d/yyyy" (built-in Excel date format, always recognised as date by readers)
    private const int DATE_NUM_FMT_ID = 14;
    // Style index 0 = General (default), style index 1 = date format
    private const int DATE_STYLE_INDEX = 1;

    private readonly ZipArchive archive;
    private readonly XlsxSharedStringTable sharedStrings = new();
    private readonly XlsxStyleRegistry styleRegistry = new();
    private readonly List<string> sheetNames = [];
    private readonly ExcelInjectionProtection injectionProtection;
    private bool disposed;

    /// <summary>Opens a new .xlsx writer over the given stream in Create mode.</summary>
    /// <param name="stream">Writable stream to receive the .xlsx data.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is not closed on Dispose.</param>
    /// <param name="injectionProtection">
    /// Controls how string cells beginning with formula-trigger characters are sanitised.
    /// Defaults to <see cref="ExcelInjectionProtection.EscapeWithApostrophe"/>.
    /// </param>
    public XlsxWriter(Stream stream, bool leaveOpen = false, ExcelInjectionProtection injectionProtection = ExcelInjectionProtection.EscapeWithApostrophe)
    {
        archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: leaveOpen);
        this.injectionProtection = injectionProtection;
    }

    // Maximum sheet name length per Excel specification.
    private const int MAX_SHEET_NAME_LENGTH = 31;

    // Characters not allowed in Excel sheet names.
    private static ReadOnlySpan<char> IllegalSheetNameChars => ['\\', '/', '?', '*', '[', ']', ':'];

    /// <summary>Registers a style in the stylesheet registry and returns its index.</summary>
    /// <param name="style">The cell style to register.</param>
    /// <returns>The resolved 0-based style index.</returns>
    public int RegisterStyle(ExcelStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return styleRegistry.RegisterStyle(style);
    }

    /// <summary>Starts a new sheet and returns a writer for it.</summary>
    /// <param name="name">The worksheet name (max 31 chars, no illegal characters).</param>
    /// <returns>A <see cref="SheetWriter"/> for writing rows and cells.</returns>
    public SheetWriter StartSheet(string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateSheetName(name);

        int sheetIndex = sheetNames.Count + 1;
        sheetNames.Add(name);

        var entryName = $"xl/worksheets/sheet{sheetIndex}.xml";
        var entry = archive.CreateEntry(entryName);
        return new SheetWriter(entry.Open(), sharedStrings, injectionProtection);
    }

    /// <summary>Finalises the workbook by writing package-level XML parts and disposes the archive.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        try
        {
            WriteContentTypes();
            WriteRootRels();
            WriteWorkbook();
            WriteWorkbookRels();
            WriteSharedStrings();
            WriteStyles();
        }
        finally
        {
            archive.Dispose();
        }
    }

    // --- zip part writers ---

    private void WriteContentTypes()
    {
        using var xmlWriter = OpenXmlWriter("[Content_Types].xml");

        xmlWriter.WriteStartDocument(standalone: true);
        xmlWriter.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");

        WriteEmptyElement(xmlWriter, "Default",
            ("Extension", "rels"),
            ("ContentType", "application/vnd.openxmlformats-package.relationships+xml"));

        WriteEmptyElement(xmlWriter, "Default",
            ("Extension", "xml"),
            ("ContentType", "application/xml"));

        WriteEmptyElement(xmlWriter, "Override",
            ("PartName", "/xl/workbook.xml"),
            ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));

        WriteEmptyElement(xmlWriter, "Override",
            ("PartName", "/xl/sharedStrings.xml"),
            ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"));

        WriteEmptyElement(xmlWriter, "Override",
            ("PartName", "/xl/styles.xml"),
            ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"));

        for (int i = 1; i <= sheetNames.Count; i++)
        {
            WriteEmptyElement(xmlWriter, "Override",
                ("PartName", $"/xl/worksheets/sheet{i}.xml"),
                ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"));
        }

        xmlWriter.WriteEndElement(); // Types
        xmlWriter.WriteEndDocument();
    }

    private void WriteRootRels()
    {
        using var xmlWriter = OpenXmlWriter("_rels/.rels");
        const string ns = "http://schemas.openxmlformats.org/package/2006/relationships";

        xmlWriter.WriteStartDocument(standalone: true);
        xmlWriter.WriteStartElement("Relationships", ns);

        WriteEmptyElement(xmlWriter, "Relationship",
            ("Id", "rId1"),
            ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
            ("Target", "xl/workbook.xml"));

        xmlWriter.WriteEndElement();
        xmlWriter.WriteEndDocument();
    }

    private void WriteWorkbook()
    {
        using var xmlWriter = OpenXmlWriter("xl/workbook.xml");
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        xmlWriter.WriteStartDocument(standalone: true);
        xmlWriter.WriteStartElement("workbook", ns);
        xmlWriter.WriteAttributeString("xmlns", "r", null, rNs);

        xmlWriter.WriteStartElement("sheets", ns);
        for (int i = 0; i < sheetNames.Count; i++)
        {
            xmlWriter.WriteStartElement("sheet", ns);
            xmlWriter.WriteAttributeString("name", sheetNames[i]);
            xmlWriter.WriteAttributeString("sheetId", (i + 1).ToString());
            xmlWriter.WriteAttributeString("id", rNs, $"rId{i + 1}");
            xmlWriter.WriteEndElement();
        }
        xmlWriter.WriteEndElement(); // sheets

        xmlWriter.WriteEndElement(); // workbook
        xmlWriter.WriteEndDocument();
    }

    private void WriteWorkbookRels()
    {
        using var xmlWriter = OpenXmlWriter("xl/_rels/workbook.xml.rels");
        const string ns = "http://schemas.openxmlformats.org/package/2006/relationships";

        xmlWriter.WriteStartDocument(standalone: true);
        xmlWriter.WriteStartElement("Relationships", ns);

        for (int i = 1; i <= sheetNames.Count; i++)
        {
            WriteEmptyElement(xmlWriter, "Relationship",
                ("Id", $"rId{i}"),
                ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                ("Target", $"worksheets/sheet{i}.xml"));
        }

        int ssId = sheetNames.Count + 1;
        WriteEmptyElement(xmlWriter, "Relationship",
            ("Id", $"rId{ssId}"),
            ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings"),
            ("Target", "sharedStrings.xml"));

        int stylesId = sheetNames.Count + 2;
        WriteEmptyElement(xmlWriter, "Relationship",
            ("Id", $"rId{stylesId}"),
            ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            ("Target", "styles.xml"));

        xmlWriter.WriteEndElement();
        xmlWriter.WriteEndDocument();
    }

    private void WriteSharedStrings()
    {
        using var xmlWriter = OpenXmlWriter("xl/sharedStrings.xml");
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var strings = sharedStrings.Strings;
        xmlWriter.WriteStartDocument(standalone: true);
        xmlWriter.WriteStartElement("sst", ns);
        xmlWriter.WriteAttributeString("count", strings.Count.ToString());
        xmlWriter.WriteAttributeString("uniqueCount", strings.Count.ToString());

        foreach (var s in strings)
        {
            xmlWriter.WriteStartElement("si", ns);
            xmlWriter.WriteStartElement("t", ns);
            // Preserve leading/trailing whitespace with xml:space="preserve"
            if (s.Length > 0 && (s[0] == ' ' || s[^1] == ' '))
                xmlWriter.WriteAttributeString("xml", "space", null, "preserve");
            xmlWriter.WriteString(s);
            xmlWriter.WriteEndElement(); // t
            xmlWriter.WriteEndElement(); // si
        }

        xmlWriter.WriteEndElement(); // sst
        xmlWriter.WriteEndDocument();
    }

    private void WriteStyles()
    {
        using var xmlWriter = OpenXmlWriter("xl/styles.xml");
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        xmlWriter.WriteStartDocument(standalone: true);
        xmlWriter.WriteStartElement("styleSheet", ns);

        // 1. Custom Number Formats
        var numFmts = styleRegistry.NumberFormats;
        if (numFmts.Count > 0)
        {
            xmlWriter.WriteStartElement("numFmts", ns);
            xmlWriter.WriteAttributeString("count", numFmts.Count.ToString());
            for (int i = 0; i < numFmts.Count; i++)
            {
                xmlWriter.WriteStartElement("numFmt", ns);
                xmlWriter.WriteAttributeString("numFmtId", (164 + i).ToString());
                xmlWriter.WriteAttributeString("formatCode", numFmts[i]);
                xmlWriter.WriteEndElement();
            }
            xmlWriter.WriteEndElement(); // numFmts
        }

        // 2. Fonts
        var fonts = styleRegistry.Fonts;
        xmlWriter.WriteStartElement("fonts", ns);
        xmlWriter.WriteAttributeString("count", fonts.Count.ToString());
        foreach (var font in fonts)
        {
            xmlWriter.WriteStartElement("font", ns);

            if (font.Bold)
            {
                xmlWriter.WriteStartElement("b", ns);
                xmlWriter.WriteEndElement();
            }
            if (font.Italic)
            {
                xmlWriter.WriteStartElement("i", ns);
                xmlWriter.WriteEndElement();
            }

            if (font.Size.HasValue)
            {
                xmlWriter.WriteStartElement("sz", ns);
                xmlWriter.WriteAttributeString("val", font.Size.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                xmlWriter.WriteEndElement();
            }

            if (!string.IsNullOrEmpty(font.Name))
            {
                xmlWriter.WriteStartElement("name", ns);
                xmlWriter.WriteAttributeString("val", font.Name);
                xmlWriter.WriteEndElement();
            }

            if (!string.IsNullOrEmpty(font.Color))
            {
                var colorHex = font.Color;
                if (colorHex.Length == 6)
                    colorHex = "FF" + colorHex; // Prepend alpha channel
                xmlWriter.WriteStartElement("color", ns);
                xmlWriter.WriteAttributeString("rgb", colorHex);
                xmlWriter.WriteEndElement();
            }

            xmlWriter.WriteEndElement(); // font
        }
        xmlWriter.WriteEndElement(); // fonts

        // 3. Fills
        var fills = styleRegistry.Fills;
        xmlWriter.WriteStartElement("fills", ns);
        xmlWriter.WriteAttributeString("count", fills.Count.ToString());
        foreach (var fill in fills)
        {
            xmlWriter.WriteStartElement("fill", ns);
            xmlWriter.WriteStartElement("patternFill", ns);
            xmlWriter.WriteAttributeString("patternType", fill.PatternType);

            if (!string.IsNullOrEmpty(fill.ForegroundColor))
            {
                var colorHex = fill.ForegroundColor;
                if (colorHex.Length == 6)
                    colorHex = "FF" + colorHex;
                xmlWriter.WriteStartElement("fgColor", ns);
                xmlWriter.WriteAttributeString("rgb", colorHex);
                xmlWriter.WriteEndElement();
            }

            if (!string.IsNullOrEmpty(fill.BackgroundColor))
            {
                var colorHex = fill.BackgroundColor;
                if (colorHex.Length == 6)
                    colorHex = "FF" + colorHex;
                xmlWriter.WriteStartElement("bgColor", ns);
                xmlWriter.WriteAttributeString("rgb", colorHex);
                xmlWriter.WriteEndElement();
            }

            xmlWriter.WriteEndElement(); // patternFill
            xmlWriter.WriteEndElement(); // fill
        }
        xmlWriter.WriteEndElement(); // fills

        // 4. Borders
        var borders = styleRegistry.Borders;
        xmlWriter.WriteStartElement("borders", ns);
        xmlWriter.WriteAttributeString("count", borders.Count.ToString());
        foreach (var border in borders)
        {
            xmlWriter.WriteStartElement("border", ns);

            WriteBorderSide(xmlWriter, ns, "left", border.Left);
            WriteBorderSide(xmlWriter, ns, "right", border.Right);
            WriteBorderSide(xmlWriter, ns, "top", border.Top);
            WriteBorderSide(xmlWriter, ns, "bottom", border.Bottom);
            xmlWriter.WriteStartElement("diagonal", ns);
            xmlWriter.WriteEndElement();

            xmlWriter.WriteEndElement(); // border
        }
        xmlWriter.WriteEndElement(); // borders

        // 5. cellStyleXfs (default Normal style xf)
        xmlWriter.WriteStartElement("cellStyleXfs", ns);
        xmlWriter.WriteAttributeString("count", "1");
        xmlWriter.WriteStartElement("xf", ns);
        xmlWriter.WriteAttributeString("numFmtId", "0");
        xmlWriter.WriteAttributeString("fontId", "0");
        xmlWriter.WriteAttributeString("fillId", "0");
        xmlWriter.WriteAttributeString("borderId", "0");
        xmlWriter.WriteEndElement();
        xmlWriter.WriteEndElement(); // cellStyleXfs

        // 6. cellXfs
        var xfs = styleRegistry.Xfs;
        xmlWriter.WriteStartElement("cellXfs", ns);
        xmlWriter.WriteAttributeString("count", xfs.Count.ToString());
        foreach (var xf in xfs)
        {
            xmlWriter.WriteStartElement("xf", ns);
            xmlWriter.WriteAttributeString("numFmtId", xf.NumFmtId.ToString());
            xmlWriter.WriteAttributeString("fontId", xf.FontId.ToString());
            xmlWriter.WriteAttributeString("fillId", xf.FillId.ToString());
            xmlWriter.WriteAttributeString("borderId", xf.BorderId.ToString());
            xmlWriter.WriteAttributeString("xfId", "0");

            if (xf.NumFmtId > 0)
                xmlWriter.WriteAttributeString("applyNumberFormat", "1");
            if (xf.FontId > 0)
                xmlWriter.WriteAttributeString("applyFont", "1");
            if (xf.FillId > 0)
                xmlWriter.WriteAttributeString("applyFill", "1");
            if (xf.BorderId > 0)
                xmlWriter.WriteAttributeString("applyBorder", "1");

            if (xf.Alignment is not null)
            {
                xmlWriter.WriteAttributeString("applyAlignment", "1");
                xmlWriter.WriteStartElement("alignment", ns);

                if (xf.Alignment.Horizontal.HasValue && xf.Alignment.Horizontal.Value != ExcelHorizontalAlignment.General)
                {
                    var horizStr = xf.Alignment.Horizontal.Value switch
                    {
                        ExcelHorizontalAlignment.Left => "left",
                        ExcelHorizontalAlignment.Center => "center",
                        ExcelHorizontalAlignment.Right => "right",
                        ExcelHorizontalAlignment.Fill => "fill",
                        ExcelHorizontalAlignment.Justify => "justify",
                        ExcelHorizontalAlignment.CenterContinuous => "centerContinuous",
                        ExcelHorizontalAlignment.Distributed => "distributed",
                        _ => "general"
                    };
                    xmlWriter.WriteAttributeString("horizontal", horizStr);
                }

                if (xf.Alignment.Vertical.HasValue && xf.Alignment.Vertical.Value != ExcelVerticalAlignment.Bottom)
                {
                    var vertStr = xf.Alignment.Vertical.Value switch
                    {
                        ExcelVerticalAlignment.Top => "top",
                        ExcelVerticalAlignment.Center => "center",
                        ExcelVerticalAlignment.Justify => "justify",
                        ExcelVerticalAlignment.Distributed => "distributed",
                        _ => "bottom"
                    };
                    xmlWriter.WriteAttributeString("vertical", vertStr);
                }

                if (xf.Alignment.WrapText.HasValue)
                {
                    xmlWriter.WriteAttributeString("wrapText", xf.Alignment.WrapText.Value ? "1" : "0");
                }

                xmlWriter.WriteEndElement(); // alignment
            }

            xmlWriter.WriteEndElement(); // xf
        }
        xmlWriter.WriteEndElement(); // cellXfs

        // 7. cellStyles
        xmlWriter.WriteStartElement("cellStyles", ns);
        xmlWriter.WriteAttributeString("count", "1");
        xmlWriter.WriteStartElement("cellStyle", ns);
        xmlWriter.WriteAttributeString("name", "Normal");
        xmlWriter.WriteAttributeString("xfId", "0");
        xmlWriter.WriteAttributeString("builtinId", "0");
        xmlWriter.WriteEndElement();
        xmlWriter.WriteEndElement(); // cellStyles

        xmlWriter.WriteEndElement(); // styleSheet
        xmlWriter.WriteEndDocument();
    }

    private static void WriteBorderSide(XmlWriter w, string ns, string name, ExcelBorderItem? borderItem)
    {
        w.WriteStartElement(name, ns);
        if (borderItem is not null && borderItem.Style != ExcelBorderStyle.None)
        {
            var styleStr = borderItem.Style switch
            {
                ExcelBorderStyle.Thin => "thin",
                ExcelBorderStyle.Medium => "medium",
                ExcelBorderStyle.Dashed => "dashed",
                ExcelBorderStyle.Dotted => "dotted",
                ExcelBorderStyle.Thick => "thick",
                ExcelBorderStyle.Double => "double",
                ExcelBorderStyle.Hair => "hair",
                ExcelBorderStyle.MediumDashed => "mediumDashed",
                ExcelBorderStyle.DashDot => "dashDot",
                ExcelBorderStyle.MediumDashDot => "mediumDashDot",
                ExcelBorderStyle.DashDotDot => "dashDotDot",
                ExcelBorderStyle.MediumDashDotDot => "mediumDashDotDot",
                ExcelBorderStyle.SlantedDashDot => "slantedDashDot",
                _ => "none"
            };
            w.WriteAttributeString("style", styleStr);

            if (!string.IsNullOrEmpty(borderItem.Color))
            {
                var colorHex = borderItem.Color;
                if (colorHex.Length == 6)
                    colorHex = "FF" + colorHex;
                w.WriteStartElement("color", ns);
                w.WriteAttributeString("rgb", colorHex);
                w.WriteEndElement();
            }
        }
        w.WriteEndElement();
    }

    // Opens a ZipArchive entry and wraps it in an XmlWriter with UTF-8 encoding.
    private XmlWriter OpenXmlWriter(string entryName)
    {
        var entry = archive.CreateEntry(entryName);
        var stream = entry.Open();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = true
        };
        return XmlWriter.Create(stream, settings);
    }

    private static void WriteEmptyElement(XmlWriter w, string localName, params (string Name, string Value)[] attributes)
    {
        w.WriteStartElement(localName);
        foreach (var (name, value) in attributes)
            w.WriteAttributeString(name, value);
        w.WriteEndElement();
    }

    // --- sheet name validation ---

    private void ValidateSheetName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (name.Length > MAX_SHEET_NAME_LENGTH)
            throw new ExcelException($"Sheet name '{name}' exceeds the maximum length of {MAX_SHEET_NAME_LENGTH} characters.");

        if (name.AsSpan().IndexOfAny(IllegalSheetNameChars) >= 0)
            throw new ExcelException($"Sheet name '{name}' contains illegal characters. The characters \\, /, ?, *, [, ], : are not allowed.");

        if (name[0] == '\'' || name[^1] == '\'')
            throw new ExcelException($"Sheet name '{name}' must not start or end with an apostrophe.");

        foreach (var existing in sheetNames)
        {
            if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                throw new ExcelException($"A sheet named '{name}' already exists. Sheet names must be unique (case-insensitive).");
        }
    }

    // --- column letter helper ---

    // Cache for column letters A..XFD (max 16384 columns in Excel).
    // Built lazily on first use per column index. Common columns (A-Z) avoid all allocation after first call.
    private static readonly string[] columnLetterCache = new string[16384];

    // Converts a 0-based column index to an Excel column letter (A, B, …, Z, AA, AB, …).
    internal static string GetColumnLetter(int columnIndex)
    {
        if ((uint)columnIndex < (uint)columnLetterCache.Length)
        {
            // Benign race: concurrent threads may both compute and write the same value.
            // Reference writes are atomic in .NET; Volatile.Read prevents stale cache reads.
            var cached = Volatile.Read(ref columnLetterCache[columnIndex]);
            if (cached is not null)
                return cached;
            var value = BuildColumnLetter(columnIndex);
            Volatile.Write(ref columnLetterCache[columnIndex], value);
            return value;
        }

        return BuildColumnLetter(columnIndex);
    }

    private static string BuildColumnLetter(int columnIndex)
    {
        // Max 3 chars for Excel columns (A..XFD)
        Span<char> buf = stackalloc char[3];
        int pos = 3;
        int index = columnIndex;
        do
        {
            buf[--pos] = (char)('A' + index % 26);
            index = index / 26 - 1;
        }
        while (index >= 0);
        return new string(buf[pos..]);
    }

    // Builds a cell reference (e.g. "B3") into the provided buffer and returns the length written.
    // Buffer must be at least 10 chars (3 for column + 7 for row number up to 1048576).
    internal static int FormatCellRef(Span<char> buffer, int columnIndex0Based, int rowNumber)
    {
        var colLetter = GetColumnLetter(columnIndex0Based);
        colLetter.AsSpan().CopyTo(buffer);
        int pos = colLetter.Length;
        rowNumber.TryFormat(buffer[pos..], out int written);
        return pos + written;
    }

    // =========================================================================
    // SheetWriter — writes a single xl/worksheets/sheetN.xml entry
    // =========================================================================

    // Writes cells into a single worksheet.
    // Call WriteHeaderRow (optional), then for each data row:
    //   StartRow → WriteCellXxx (one per column) → EndRow
    // Call Close() when all rows are written.
    /// <summary>
    /// Writes cells into a single Excel worksheet within an <see cref="XlsxWriter"/>.
    /// Uses direct UTF-8 byte output instead of XmlWriter for zero per-cell allocations.
    /// </summary>
    public sealed class SheetWriter : IDisposable
    {
        // Pre-encoded XML fragments as UTF-8 byte literals
        private static readonly byte[] xmlHeader = "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"u8.ToArray();
        private static readonly byte[] xmlFooterSheetDataClose = "</sheetData>"u8.ToArray();
        private static readonly byte[] xmlFooterWorksheetClose = "</worksheet>"u8.ToArray();
        private static readonly byte[] rowOpen = "<row r=\""u8.ToArray();
        private static readonly byte[] rowClose = "</row>"u8.ToArray();
        private static readonly byte[] cellStringOpen = "<c r=\""u8.ToArray();
        private static readonly byte[] cellStringTypeAttr = "\" t=\"s\"><v>"u8.ToArray();
        private static readonly byte[] cellNumOpen = "\" ><v>"u8.ToArray(); // no t attr for numeric
        private static readonly byte[] cellBoolOpen = "\" t=\"b\"><v>"u8.ToArray();
        private static readonly byte[] cellDateOpen = "\" s=\"1\"><v>"u8.ToArray();
        private static readonly byte[] cellValueClose = "</v></c>"u8.ToArray();
        private static readonly byte[] cellEmptyClose = "\" />"u8.ToArray();
        private static readonly byte[] quoteClose = "\">"u8.ToArray();

        private const int BUFFER_SIZE = 65536; // 64 KB output buffer

        private readonly Stream stream;
        private readonly XlsxSharedStringTable sharedStrings;
        private readonly ExcelInjectionProtection injectionProtection;
        private readonly byte[] buffer;
        private int bufferPos;
        private long totalBytesWritten;
        private bool rowIsOpen;
        private bool closed;
        private int currentRowNumber;
        private List<string>? mergedCells;

        /// <summary>Gets the total number of uncompressed bytes written to the worksheet stream so far.</summary>
        public long BytesWritten => totalBytesWritten + bufferPos;

        // Pre-formatted column letter bytes (cached per column, lazily built)
        private static readonly byte[][] columnLetterBytes = new byte[16384][];

        internal SheetWriter(Stream stream, XlsxSharedStringTable sharedStrings, ExcelInjectionProtection injectionProtection = ExcelInjectionProtection.EscapeWithApostrophe)
        {
            this.stream = stream;
            this.sharedStrings = sharedStrings;
            this.injectionProtection = injectionProtection;
            buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
            bufferPos = 0;

            WriteRaw(xmlHeader);
        }

        /// <summary>Writes row 1 as a header row using shared string cells.</summary>
        /// <param name="headers">Column header names.</param>
        public void WriteHeaderRow(string[] headers)
        {
            StartRow(1);
            for (int col = 0; col < headers.Length; col++)
                WriteCellString(col + 1, headers[col]);
            EndRow();
        }

        /// <summary>Opens a new row element with an optional outline level.</summary>
        /// <param name="rowNumber">The 1-based row number.</param>
        /// <param name="outlineLevel">The outline level (group nesting depth) for this row, 0 to 7. Defaults to 0 (no outlining).</param>
        public void StartRow(int rowNumber, int outlineLevel = 0)
        {
            if (rowIsOpen)
                EndRow();

            currentRowNumber = rowNumber;
            WriteRaw(rowOpen);                     // <row r="
            WriteInt(rowNumber);                   // 123
            if (outlineLevel > 0)
            {
                WriteRaw(" outlineLevel=\""u8);
                WriteInt(outlineLevel);
                WriteRaw("\""u8);
            }
            WriteRaw(quoteClose);                  // ">
            rowIsOpen = true;
        }

        /// <summary>Writes a shared-string cell.</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="value">The string value.</param>
        /// <param name="styleIndex">The optional style index.</param>
        public void WriteCellString(int columnIndex, string value, int? styleIndex = null)
        {
            string sanitised = ApplyInjectionProtection(value);
            int ssIndex = sharedStrings.GetOrAdd(sanitised);
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            if (styleIndex is not null)
            {
                WriteRaw("\" s=\""u8);
                WriteInt(styleIndex.Value);
                WriteRaw("\" t=\"s\"><v>"u8);
            }
            else
            {
                WriteRaw(cellStringTypeAttr);          // " t="s"><v>
            }
            WriteInt(ssIndex);                     // 42
            WriteRaw(cellValueClose);              // </v></c>
        }

        /// <summary>Writes a shared-string cell from a span of characters.</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="value">The character span value.</param>
        /// <param name="styleIndex">The optional style index.</param>
        public void WriteCellString(int columnIndex, ReadOnlySpan<char> value, int? styleIndex = null)
        {
            int ssIndex;
            if (injectionProtection != ExcelInjectionProtection.None && IsDangerousLeadingChar(value))
            {
                string sanitised = ApplyInjectionProtectionSpan(value);
                ssIndex = sharedStrings.GetOrAdd(sanitised);
            }
            else
            {
                ssIndex = sharedStrings.GetOrAdd(value);
            }

            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            if (styleIndex is not null)
            {
                WriteRaw("\" s=\""u8);
                WriteInt(styleIndex.Value);
                WriteRaw("\" t=\"s\"><v>"u8);
            }
            else
            {
                WriteRaw(cellStringTypeAttr);          // " t="s"><v>
            }
            WriteInt(ssIndex);                     // 42
            WriteRaw(cellValueClose);              // </v></c>
        }

        private string ApplyInjectionProtectionSpan(ReadOnlySpan<char> value)
        {
            if (injectionProtection == ExcelInjectionProtection.None || value.IsEmpty)
                return value.ToString();

            if (!IsDangerousLeadingChar(value))
                return value.ToString();

            return injectionProtection switch
            {
                ExcelInjectionProtection.EscapeWithApostrophe => "'" + value.ToString(),
                ExcelInjectionProtection.Sanitize => StripDangerousPrefix(value.ToString()),
                ExcelInjectionProtection.Reject => throw new ExcelException(
                    $"Excel injection detected: cell value starts with dangerous character '{value[0]}'."),
                ExcelInjectionProtection.None => value.ToString(),
                _ => value.ToString(),
            };
        }

        // Applies the configured injection protection. The check is a single character comparison
        // for non-dangerous values, so the cost on the common path is negligible.
        private string ApplyInjectionProtection(string value)
        {
            if (injectionProtection == ExcelInjectionProtection.None || string.IsNullOrEmpty(value))
                return value;

            if (!IsDangerousLeadingChar(value))
                return value;

            return injectionProtection switch
            {
                ExcelInjectionProtection.EscapeWithApostrophe => "'" + value,
                ExcelInjectionProtection.Sanitize => StripDangerousPrefix(value),
                ExcelInjectionProtection.Reject => throw new ExcelException(
                    $"Excel injection detected: cell value starts with dangerous character '{value[0]}'."),
                ExcelInjectionProtection.None => value,
                _ => value,
            };
        }

        private static bool IsDangerousLeadingChar(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty) return false;
            char first = value[0];
            switch (first)
            {
                case '=':
                case '@':
                case '\t':
                case '\r':
                    return true;
                case '-':
                case '+':
                    if (value.Length == 1) return false;
                    char second = value[1];
                    return !((uint)(second - '0') <= 9 || second == '.');
                default:
                    return false;
            }
        }

        private static string StripDangerousPrefix(string value)
        {
            int start = 0;
            ReadOnlySpan<char> span = value.AsSpan();
            while (start < span.Length && IsDangerousLeadingChar(span[start..]))
            {
                start++;
            }
            return start == 0 ? value : value[start..];
        }

        /// <summary>Writes a numeric cell.</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="value">The numeric value.</param>
        /// <param name="styleIndex">The optional style index.</param>
        public void WriteCellNumber(int columnIndex, double value, int? styleIndex = null)
        {
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            if (styleIndex is not null)
            {
                WriteRaw("\" s=\""u8);
                WriteInt(styleIndex.Value);
                WriteRaw("\"><v>"u8);
            }
            else
            {
                WriteRaw(cellNumOpen);                 // " ><v>
            }
            WriteDouble(value);                    // 3.14159
            WriteRaw(cellValueClose);              // </v></c>
        }

        /// <summary>Writes a boolean cell (1 for true, 0 for false).</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="value">The boolean value.</param>
        /// <param name="styleIndex">The optional style index.</param>
        public void WriteCellBoolean(int columnIndex, bool value, int? styleIndex = null)
        {
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            if (styleIndex is not null)
            {
                WriteRaw("\" s=\""u8);
                WriteInt(styleIndex.Value);
                WriteRaw("\" t=\"b\"><v>"u8);
            }
            else
            {
                WriteRaw(cellBoolOpen);                // " t="b"><v>
            }
            buffer[bufferPos++] = value ? (byte)'1' : (byte)'0';
            WriteRaw(cellValueClose);              // </v></c>
        }

        /// <summary>Writes a date cell as an OA date serial number with a date style.</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="value">The date/time value.</param>
        /// <param name="styleIndex">The optional style index. Defaults to 1 (date style).</param>
        public void WriteCellDate(int columnIndex, DateTime value, int? styleIndex = null)
        {
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3

            int actualStyleIndex = styleIndex ?? 1;
            WriteRaw("\" s=\""u8);
            WriteInt(actualStyleIndex);
            WriteRaw("\"><v>"u8);

            WriteDouble(value.ToOADate());         // 45123.5
            WriteRaw(cellValueClose);              // </v></c>
        }

        /// <summary>Writes an empty cell (no value element).</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="styleIndex">The optional style index.</param>
        public void WriteCellEmpty(int columnIndex, int? styleIndex = null)
        {
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            if (styleIndex is not null)
            {
                WriteRaw("\" s=\""u8);
                WriteInt(styleIndex.Value);
                WriteRaw("\" />"u8);
            }
            else
            {
                WriteRaw(cellEmptyClose);              // " />
            }
        }

        /// <summary>Merges a range of cells (e.g., "A1:B2").</summary>
        /// <param name="range">The cell range in A1 notation.</param>
        public void MergeCells(string range)
        {
            ArgumentException.ThrowIfNullOrEmpty(range);
            mergedCells ??= [];
            mergedCells.Add(range);
        }

        /// <summary>Merges a range of cells specified by 1-based start/end columns and rows.</summary>
        /// <param name="startColumn">The 1-based starting column index.</param>
        /// <param name="startRow">The 1-based starting row number.</param>
        /// <param name="endColumn">The 1-based ending column index.</param>
        /// <param name="endRow">The 1-based ending row number.</param>
        public void MergeCells(int startColumn, int startRow, int endColumn, int endRow)
        {
            if (startColumn <= 0 || startRow <= 0 || endColumn <= 0 || endRow <= 0)
                throw new ArgumentException("Column and row numbers must be 1-based and positive.");

            Span<char> startBuf = stackalloc char[15];
            int startLen = FormatCellRef(startBuf, startColumn - 1, startRow);

            Span<char> endBuf = stackalloc char[15];
            int endLen = FormatCellRef(endBuf, endColumn - 1, endRow);

            var range = $"{startBuf[..startLen]}:{endBuf[..endLen]}";
            MergeCells(range);
        }

        /// <summary>Closes the current row element.</summary>
        public void EndRow()
        {
            if (!rowIsOpen)
                return;

            WriteRaw(rowClose);                    // </row>
            rowIsOpen = false;
        }

        /// <summary>Finalises the worksheet XML. Must be called after all rows are written.</summary>
        public void Close()
        {
            if (closed)
                return;

            closed = true;

            if (rowIsOpen)
                EndRow();

            WriteRaw(xmlFooterSheetDataClose);

            if (mergedCells is not null && mergedCells.Count > 0)
            {
                WriteRaw("<mergeCells count=\""u8);
                WriteInt(mergedCells.Count);
                WriteRaw("\">"u8);

                Span<byte> rangeBytes = stackalloc byte[32];

                foreach (var range in mergedCells)
                {
                    WriteRaw("<mergeCell ref=\""u8);

                    var len = range.Length;
                    if (len <= rangeBytes.Length)
                    {
                        for (int i = 0; i < len; i++)
                            rangeBytes[i] = (byte)range[i];
                        WriteRaw(rangeBytes[..len]);
                    }
                    else
                    {
                        var bytes = Encoding.UTF8.GetBytes(range);
                        WriteRaw(bytes);
                    }

                    WriteRaw("\"/>"u8);
                }

                WriteRaw("</mergeCells>"u8);
            }

            WriteRaw(xmlFooterWorksheetClose);
            Flush();
        }

        /// <summary>Calls <see cref="Close"/> if not already done and disposes the underlying writer.</summary>
        public void Dispose()
        {
            Close();
            ArrayPool<byte>.Shared.Return(buffer);
            stream.Dispose();
        }

        // --- zero-alloc formatting helpers ---

        private void WriteRaw(ReadOnlySpan<byte> data)
        {
            if (bufferPos + data.Length > buffer.Length)
                Flush();

            // Handle data larger than buffer (unlikely but safe)
            if (data.Length > buffer.Length)
            {
                stream.Write(data);
                return;
            }

            data.CopyTo(buffer.AsSpan(bufferPos));
            bufferPos += data.Length;
        }

        private void WriteInt(int value)
        {
            EnsureSpace(11); // max int digits + sign
            value.TryFormat(buffer.AsSpan(bufferPos), out int written);
            bufferPos += written;
        }

        private void WriteDouble(double value)
        {
            EnsureSpace(32);
            value.TryFormat(buffer.AsSpan(bufferPos), out int written, provider: System.Globalization.CultureInfo.InvariantCulture);
            bufferPos += written;
        }

        private void WriteCellRef(int columnIndex)
        {
            // Column letter bytes (cached)
            int col0 = columnIndex - 1;
            var colBytes = GetColumnLetterBytes(col0);
            EnsureSpace(colBytes.Length + 7); // col letters + max row digits
            colBytes.AsSpan().CopyTo(buffer.AsSpan(bufferPos));
            bufferPos += colBytes.Length;

            // Row number (direct format into buffer)
            currentRowNumber.TryFormat(buffer.AsSpan(bufferPos), out int written);
            bufferPos += written;
        }

        private static byte[] GetColumnLetterBytes(int columnIndex)
        {
            if ((uint)columnIndex < (uint)columnLetterBytes.Length)
            {
                // Benign race: concurrent threads may both compute and write the same value.
                var cached = Volatile.Read(ref columnLetterBytes[columnIndex]);
                if (cached is not null)
                    return cached;
                var value = Encoding.UTF8.GetBytes(GetColumnLetter(columnIndex));
                Volatile.Write(ref columnLetterBytes[columnIndex], value);
                return value;
            }

            return Encoding.UTF8.GetBytes(GetColumnLetter(columnIndex));
        }

        private void EnsureSpace(int needed)
        {
            if (bufferPos + needed > buffer.Length)
                Flush();
        }

        private void Flush()
        {
            if (bufferPos > 0)
            {
                stream.Write(buffer, 0, bufferPos);
                totalBytesWritten += bufferPos;
                bufferPos = 0;
            }
        }
    }
}
