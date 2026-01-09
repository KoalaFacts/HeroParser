namespace HeroParser.SeparatedValues.Reading.Rows;

public sealed partial class CsvRowReaderBuilder
{
    /// <summary>
    /// Creates a reader from CSV text for manual row-by-row reading.
    /// Uses scalar parsing only (no SIMD acceleration).
    /// </summary>
    /// <remarks>
    /// For optimal performance with SIMD acceleration, use the overload with the out parameter
    /// or use <see cref="FromFile"/> / <see cref="FromStream"/>.
    /// </remarks>
    public CsvRowReader<char> FromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        var reader = Csv.ReadFromText(csvText, GetOptions());
        SkipInitialRows(ref reader);
        return reader;
    }

    /// <summary>
    /// Creates a reader from CSV text for manual row-by-row reading.
    /// The string is encoded to UTF-8 for SIMD-accelerated parsing.
    /// </summary>
    /// <param name="csvText">The CSV text to parse.</param>
    /// <param name="textBytes">The UTF-8 encoded bytes. Must be kept alive for the duration of parsing.</param>
    /// <returns>A row reader over the UTF-8 encoded text.</returns>
    public CsvRowReader<byte> FromText(string csvText, out byte[] textBytes)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        var reader = Csv.ReadFromText(csvText, out textBytes, GetOptions());
        SkipInitialRows(ref reader);
        return reader;
    }

    /// <summary>
    /// Creates a reader from a CSV file, reading as raw UTF-8 bytes for efficiency.
    /// </summary>
    /// <param name="path">Path to the CSV file.</param>
    /// <param name="fileBytes">The file bytes. Must be kept alive for the duration of parsing.</param>
    /// <returns>A row reader over the file bytes.</returns>
    /// <remarks>
    /// This method reads the file directly as UTF-8 bytes, avoiding the 2x memory overhead
    /// of UTF-16 string conversion. Use <see cref="CsvRow{T}.GetString(int)"/> to lazily
    /// materialize strings from individual columns.
    ///
    /// The returned byte array must be kept alive for the duration of parsing.
    /// For very large files that don't fit in memory, use streaming APIs instead.
    /// </remarks>
    public CsvRowReader<byte> FromFile(string path, out byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        var reader = Csv.ReadFromFile(path, out fileBytes, GetOptions());
        SkipInitialRows(ref reader);
        return reader;
    }

    /// <summary>
    /// Creates a reader from a stream, reading as raw UTF-8 bytes for efficiency.
    /// </summary>
    /// <param name="stream">The stream containing UTF-8 CSV data.</param>
    /// <param name="streamBytes">The stream bytes. Must be kept alive for the duration of parsing.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <returns>A row reader over the stream bytes.</returns>
    /// <remarks>
    /// This method reads the entire stream into memory as UTF-8 bytes, avoiding the 2x memory
    /// overhead of UTF-16 string conversion. Use <see cref="CsvRow{T}.GetString(int)"/> to lazily
    /// materialize strings from individual columns.
    ///
    /// For very large streams that don't fit in memory, use streaming APIs instead.
    /// </remarks>
    public CsvRowReader<byte> FromStream(Stream stream, out byte[] streamBytes, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = Csv.ReadFromStream(stream, out streamBytes, GetOptions(), leaveOpen);
        SkipInitialRows(ref reader);
        return reader;
    }

    private void SkipInitialRows(ref CsvRowReader<char> reader)
    {
        for (int i = 0; i < skipRows && reader.MoveNext(); i++) { }
    }

    private void SkipInitialRows(ref CsvRowReader<byte> reader)
    {
        for (int i = 0; i < skipRows && reader.MoveNext(); i++) { }
    }
}
