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
internal sealed class XlsxWriter : IDisposable
{
    // numFmtId 14 = "m/d/yyyy" (built-in Excel date format, always recognised as date by readers)
    private const int DATE_NUM_FMT_ID = 14;
    // Style index 0 = General (default), style index 1 = date format
    private const int DATE_STYLE_INDEX = 1;

    private readonly ZipArchive archive;
    private readonly XlsxSharedStringTable sharedStrings = new();
    private readonly List<string> sheetNames = [];
    private bool disposed;

    // Opens a new .xlsx writer over <paramref name="stream"/> in Create mode.
    // <param name="stream">Writable stream to receive the .xlsx data.</param>
    // <param name="leaveOpen">When true, the stream is not closed on Dispose.</param>
    public XlsxWriter(Stream stream, bool leaveOpen = false)
    {
        archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: leaveOpen);
    }

    // Maximum sheet name length per Excel specification.
    private const int MAX_SHEET_NAME_LENGTH = 31;

    // Characters not allowed in Excel sheet names.
    private static ReadOnlySpan<char> IllegalSheetNameChars => ['\\', '/', '?', '*', '[', ']', ':'];

    // Starts a new sheet and returns a writer for it.
    // Sheets must be closed (via SheetWriter.Close()) before XlsxWriter is disposed.
    public SheetWriter StartSheet(string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateSheetName(name);

        int sheetIndex = sheetNames.Count + 1;
        sheetNames.Add(name);

        var entryName = $"xl/worksheets/sheet{sheetIndex}.xml";
        var entry = archive.CreateEntry(entryName);
        return new SheetWriter(entry.Open(), sharedStrings);
    }

    // Finalises the workbook: writes [Content_Types].xml, _rels/.rels, xl/workbook.xml,
    // xl/_rels/workbook.xml.rels, xl/sharedStrings.xml, and xl/styles.xml.
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

        // Style index 1 = built-in date format (numFmtId 14 = "m/d/yyyy").
        // No custom numFmts entry needed because 14 is a built-in Excel format.

        // cellXfs: index 0 = General, index 1 = date
        xmlWriter.WriteStartElement("cellXfs", ns);
        xmlWriter.WriteAttributeString("count", "2");

        // style index 0: General
        xmlWriter.WriteStartElement("xf", ns);
        xmlWriter.WriteAttributeString("numFmtId", "0");
        xmlWriter.WriteEndElement();

        // style index 1: date (m/d/yyyy)
        xmlWriter.WriteStartElement("xf", ns);
        xmlWriter.WriteAttributeString("numFmtId", DATE_NUM_FMT_ID.ToString());
        xmlWriter.WriteEndElement();

        xmlWriter.WriteEndElement(); // cellXfs
        xmlWriter.WriteEndElement(); // styleSheet
        xmlWriter.WriteEndDocument();
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
            return columnLetterCache[columnIndex] ??= BuildColumnLetter(columnIndex);
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
    internal sealed class SheetWriter : IDisposable
    {
        private readonly XmlWriter xmlWriter;
        private readonly XlsxSharedStringTable sharedStrings;
        private bool rowOpen;
        private bool closed;
        private int currentRowNumber;

        // Reusable buffer for cell references (max 3 col letters + 7 row digits = 10 chars)
        private readonly char[] cellRefBuffer = new char[10];

        internal SheetWriter(Stream stream, XlsxSharedStringTable sharedStrings)
        {
            this.sharedStrings = sharedStrings;

            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = false,
                CloseOutput = true
            };
            xmlWriter = XmlWriter.Create(stream, settings);

            // Open the worksheet element
            xmlWriter.WriteStartDocument(standalone: true);
            xmlWriter.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            xmlWriter.WriteStartElement("sheetData", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        }

        // Writes row 1 as a header row using shared string cells.
        // Must be called before StartRow if used.
        public void WriteHeaderRow(string[] headers)
        {
            StartRow(1);
            for (int col = 0; col < headers.Length; col++)
                WriteCellString(col + 1, headers[col]);
            EndRow();
        }

        // Opens a new row element. rowNumber is 1-based.
        public void StartRow(int rowNumber)
        {
            if (rowOpen)
                EndRow();

            currentRowNumber = rowNumber;
            xmlWriter.WriteStartElement("row", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

            Span<char> rowBuf = stackalloc char[7]; // max 1048576
            rowNumber.TryFormat(rowBuf, out int written);
            xmlWriter.WriteAttributeString("r", new string(rowBuf[..written]));
            rowOpen = true;
        }

        // Writes a shared-string cell. columnIndex is 1-based.
        public void WriteCellString(int columnIndex, string value)
        {
            int ssIndex = sharedStrings.GetOrAdd(value);

            xmlWriter.WriteStartElement("c", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            WriteCellRefAttribute(columnIndex);
            xmlWriter.WriteAttributeString("t", "s");
            xmlWriter.WriteStartElement("v", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

            Span<char> idxBuf = stackalloc char[10];
            ssIndex.TryFormat(idxBuf, out int written);
            xmlWriter.WriteString(new string(idxBuf[..written]));

            xmlWriter.WriteEndElement(); // v
            xmlWriter.WriteEndElement(); // c
        }

        // Writes a numeric cell. columnIndex is 1-based.
        public void WriteCellNumber(int columnIndex, double value)
        {
            xmlWriter.WriteStartElement("c", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            WriteCellRefAttribute(columnIndex);
            xmlWriter.WriteStartElement("v", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

            Span<char> numBuf = stackalloc char[32];
            value.TryFormat(numBuf, out int written, provider: System.Globalization.CultureInfo.InvariantCulture);
            xmlWriter.WriteString(new string(numBuf[..written]));

            xmlWriter.WriteEndElement(); // v
            xmlWriter.WriteEndElement(); // c
        }

        // Writes a boolean cell (1 for true, 0 for false). columnIndex is 1-based.
        public void WriteCellBoolean(int columnIndex, bool value)
        {
            xmlWriter.WriteStartElement("c", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            WriteCellRefAttribute(columnIndex);
            xmlWriter.WriteAttributeString("t", "b");
            xmlWriter.WriteStartElement("v", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            xmlWriter.WriteString(value ? "1" : "0");
            xmlWriter.WriteEndElement(); // v
            xmlWriter.WriteEndElement(); // c
        }

        // Writes a date cell as an OA date serial number with style index 1 (date format).
        // columnIndex is 1-based.
        public void WriteCellDate(int columnIndex, DateTime value)
        {
            double oaDate = value.ToOADate();

            xmlWriter.WriteStartElement("c", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            WriteCellRefAttribute(columnIndex);
            xmlWriter.WriteAttributeString("s", "1"); // DATE_STYLE_INDEX
            xmlWriter.WriteStartElement("v", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

            Span<char> dateBuf = stackalloc char[32];
            oaDate.TryFormat(dateBuf, out int written, provider: System.Globalization.CultureInfo.InvariantCulture);
            xmlWriter.WriteString(new string(dateBuf[..written]));

            xmlWriter.WriteEndElement(); // v
            xmlWriter.WriteEndElement(); // c
        }

        // Writes an empty cell (no value element). columnIndex is 1-based.
        public void WriteCellEmpty(int columnIndex)
        {
            xmlWriter.WriteStartElement("c", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            WriteCellRefAttribute(columnIndex);
            xmlWriter.WriteEndElement(); // c
        }

        // Closes the current row element.
        public void EndRow()
        {
            if (!rowOpen)
                return;

            xmlWriter.WriteEndElement(); // row
            rowOpen = false;
        }

        // Finalises the worksheet XML. Must be called after all rows are written.
        public void Close()
        {
            if (closed)
                return;

            closed = true;

            if (rowOpen)
                EndRow();

            xmlWriter.WriteEndElement(); // sheetData
            xmlWriter.WriteEndElement(); // worksheet
            xmlWriter.WriteEndDocument();
            xmlWriter.Flush();
        }

        // Calls Close() if not already done.
        public void Dispose()
        {
            Close();
            xmlWriter.Dispose();
        }

        // Writes the "r" attribute with a cell reference (e.g. "B3") using the reusable buffer.
        // columnIndex is 1-based.
        private void WriteCellRefAttribute(int columnIndex)
        {
            int len = FormatCellRef(cellRefBuffer, columnIndex - 1, currentRowNumber);
            xmlWriter.WriteAttributeString("r", new string(cellRefBuffer, 0, len));
        }
    }
}
