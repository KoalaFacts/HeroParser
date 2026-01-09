namespace HeroParser.SeparatedValues.Reading.Records;

public sealed partial class CsvRecordReaderBuilder<T>
{
    /// <summary>
    /// Reads records from a CSV string.
    /// Uses scalar parsing only (no SIMD acceleration).
    /// </summary>
    /// <remarks>
    /// For optimal performance with SIMD acceleration, use the overload with the out parameter
    /// or use <see cref="FromFile"/> / <see cref="FromStream"/>.
    /// </remarks>
    public CsvRecordReader<char, T> FromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        var (parserOptions, recordOptions) = GetOptions();
        return Csv.DeserializeRecords<T>(csvText, recordOptions, parserOptions);
    }

    /// <summary>
    /// Reads records from a CSV string.
    /// The string is encoded to UTF-8 for SIMD-accelerated parsing.
    /// </summary>
    /// <param name="csvText">The CSV text to parse.</param>
    /// <param name="textBytes">The UTF-8 encoded bytes. Must be kept alive for the duration of parsing.</param>
    /// <returns>A record reader over the UTF-8 encoded text.</returns>
    public CsvRecordReader<byte, T> FromText(string csvText, out byte[] textBytes)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        var (parserOptions, recordOptions) = GetOptions();
        return Csv.DeserializeRecords<T>(csvText, out textBytes, recordOptions, parserOptions);
    }

    /// <summary>
    /// Reads records from a CSV file, parsing as raw UTF-8 bytes for efficiency.
    /// </summary>
    /// <param name="path">Path to the CSV file.</param>
    /// <param name="fileBytes">The file bytes. Must be kept alive for the duration of parsing.</param>
    /// <returns>A record reader over the file bytes.</returns>
    /// <remarks>
    /// This method reads the file directly as UTF-8 bytes, avoiding the 2x memory overhead
    /// of UTF-16 string conversion. The returned byte array must be kept alive for the
    /// duration of parsing.
    ///
    /// For very large files that don't fit in memory, use streaming APIs instead.
    /// </remarks>
    public CsvRecordReader<byte, T> FromFile(string path, out byte[] fileBytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        var (parserOptions, recordOptions) = GetOptions();

        fileBytes = File.ReadAllBytes(path);
        return Csv.DeserializeRecordsFromBytes<T>(fileBytes, recordOptions, parserOptions);
    }

    /// <summary>
    /// Reads records from a stream, parsing as raw UTF-8 bytes for efficiency.
    /// </summary>
    /// <param name="stream">The stream containing UTF-8 CSV data.</param>
    /// <param name="streamBytes">The stream bytes. Must be kept alive for the duration of parsing.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <returns>A record reader over the stream bytes.</returns>
    /// <remarks>
    /// This method reads the entire stream into memory as UTF-8 bytes, avoiding the 2x memory
    /// overhead of UTF-16 string conversion.
    ///
    /// For very large streams that don't fit in memory, use streaming APIs instead.
    /// </remarks>
    public CsvRecordReader<byte, T> FromStream(Stream stream, out byte[] streamBytes, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var (parserOptions, recordOptions) = GetOptions();

        if (stream is MemoryStream ms && ms.TryGetBuffer(out var buffer))
        {
            // Fast path for MemoryStream - avoid copy if possible
            streamBytes = buffer.Array ?? ms.ToArray();
        }
        else
        {
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            streamBytes = memoryStream.ToArray();
        }

        if (!leaveOpen)
        {
            stream.Dispose();
        }

        return Csv.DeserializeRecordsFromBytes<T>(streamBytes, recordOptions, parserOptions);
    }
}
