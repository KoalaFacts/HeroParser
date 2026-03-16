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
    private readonly List<string> sheetNames = [];
    private bool disposed;

    /// <summary>Opens a new .xlsx writer over the given stream in Create mode.</summary>
    /// <param name="stream">Writable stream to receive the .xlsx data.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is not closed on Dispose.</param>
    public XlsxWriter(Stream stream, bool leaveOpen = false)
    {
        archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: leaveOpen);
    }

    // Maximum sheet name length per Excel specification.
    private const int MAX_SHEET_NAME_LENGTH = 31;

    // Characters not allowed in Excel sheet names.
    private static ReadOnlySpan<char> IllegalSheetNameChars => ['\\', '/', '?', '*', '[', ']', ':'];

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
        return new SheetWriter(entry.Open(), sharedStrings);
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
        private static readonly byte[] xmlFooter = "</sheetData></worksheet>"u8.ToArray();
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
        private readonly byte[] buffer;
        private int bufferPos;
        private bool rowIsOpen;
        private bool closed;
        private int currentRowNumber;

        // Pre-formatted column letter bytes (cached per column, lazily built)
        private static readonly byte[][] columnLetterBytes = new byte[16384][];

        internal SheetWriter(Stream stream, XlsxSharedStringTable sharedStrings)
        {
            this.stream = stream;
            this.sharedStrings = sharedStrings;
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

        /// <summary>Opens a new row element.</summary>
        /// <param name="rowNumber">The 1-based row number.</param>
        public void StartRow(int rowNumber)
        {
            if (rowIsOpen)
                EndRow();

            currentRowNumber = rowNumber;
            WriteRaw(rowOpen);                     // <row r="
            WriteInt(rowNumber);                   // 123
            WriteRaw(quoteClose);                  // ">
            rowIsOpen = true;
        }

        /// <summary>Writes a shared-string cell.</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="value">The string value.</param>
        public void WriteCellString(int columnIndex, string value)
        {
            int ssIndex = sharedStrings.GetOrAdd(value);
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            WriteRaw(cellStringTypeAttr);          // " t="s"><v>
            WriteInt(ssIndex);                     // 42
            WriteRaw(cellValueClose);              // </v></c>
        }

        /// <summary>Writes a numeric cell.</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="value">The numeric value.</param>
        public void WriteCellNumber(int columnIndex, double value)
        {
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            WriteRaw(cellNumOpen);                 // " ><v>
            WriteDouble(value);                    // 3.14159
            WriteRaw(cellValueClose);              // </v></c>
        }

        /// <summary>Writes a boolean cell (1 for true, 0 for false).</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="value">The boolean value.</param>
        public void WriteCellBoolean(int columnIndex, bool value)
        {
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            WriteRaw(cellBoolOpen);                // " t="b"><v>
            buffer[bufferPos++] = value ? (byte)'1' : (byte)'0';
            WriteRaw(cellValueClose);              // </v></c>
        }

        /// <summary>Writes a date cell as an OA date serial number with a date style.</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        /// <param name="value">The date/time value.</param>
        public void WriteCellDate(int columnIndex, DateTime value)
        {
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            WriteRaw(cellDateOpen);                // " s="1"><v>
            WriteDouble(value.ToOADate());         // 45123.5
            WriteRaw(cellValueClose);              // </v></c>
        }

        /// <summary>Writes an empty cell (no value element).</summary>
        /// <param name="columnIndex">The 1-based column index.</param>
        public void WriteCellEmpty(int columnIndex)
        {
            WriteRaw(cellStringOpen);              // <c r="
            WriteCellRef(columnIndex);             // B3
            WriteRaw(cellEmptyClose);              // " />
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

            WriteRaw(xmlFooter);
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
                bufferPos = 0;
            }
        }
    }
}
