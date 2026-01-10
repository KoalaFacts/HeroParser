using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Creates a fluent builder for reading and deserializing CSV records of type <typeparamref name="T"/>.
    /// </summary>
    public static CsvRecordReaderBuilder<T> Read<T>() where T : class, new() => new();

    /// <summary>
    /// Creates a fluent builder for manual row-by-row CSV reading.
    /// </summary>
    public static CsvRowReaderBuilder Read() => new();

    /// <summary>
    /// Creates a reader that iterates over CSV records stored in a managed <see cref="string"/>.
    /// The string is encoded to UTF-8 for SIMD-accelerated parsing.
    /// </summary>
    /// <param name="data">The CSV text to parse.</param>
    /// <param name="textBytes">The UTF-8 encoded bytes. Must be kept alive for the duration of parsing.</param>
    /// <param name="options">Optional parser options.</param>
    /// <returns>A row reader over the UTF-8 encoded text.</returns>
    public static CsvRowReader<byte> ReadFromText(string data, out byte[] textBytes, CsvParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        textBytes = System.Text.Encoding.UTF8.GetBytes(data);
        return ReadFromByteSpan(textBytes, options);
    }

    /// <summary>
    /// Creates a reader that iterates over CSV records stored in a managed <see cref="string"/>.
    /// Uses scalar parsing only (no SIMD acceleration).
    /// </summary>
    /// <remarks>
    /// For optimal performance with SIMD acceleration, use the overload with the out parameter
    /// or use <see cref="ReadFromFile"/> / <see cref="ReadFromStream"/>.
    /// </remarks>
    public static CsvRowReader<char> ReadFromText(string data, CsvParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ReadFromCharSpan(data.AsSpan(), options);
    }

    /// <summary>
    /// Creates a reader over a UTF-16 span.
    /// </summary>
    public static CsvRowReader<char> ReadFromCharSpan(ReadOnlySpan<char> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();
        return new CsvRowReader<char>(data, options);
    }

    /// <summary>
    /// Creates a reader over UTF-8 encoded CSV data.
    /// </summary>
    public static CsvRowReader<byte> ReadFromByteSpan(ReadOnlySpan<byte> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();

        // Detect UTF-16 BOMs
        if (data.Length >= 2)
        {
            if (data[0] == 0xFF && data[1] == 0xFE)
                throw new CsvException(CsvErrorCode.InvalidOptions, "UTF-16 LE encoding detected. HeroParser only supports UTF-8.");
            if (data[0] == 0xFE && data[1] == 0xFF)
                throw new CsvException(CsvErrorCode.InvalidOptions, "UTF-16 BE encoding detected. HeroParser only supports UTF-8.");
        }

        // Strip UTF-8 BOM
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            data = data[3..];

        return new CsvRowReader<byte>(data, options);
    }

    /// <summary>
    /// Deserializes CSV data from text into strongly typed records.
    /// The string is encoded to UTF-8 for SIMD-accelerated parsing.
    /// </summary>
    /// <param name="data">The CSV text to parse.</param>
    /// <param name="textBytes">The UTF-8 encoded bytes. Must be kept alive for the duration of parsing.</param>
    /// <param name="recordOptions">Optional record deserialization options.</param>
    /// <param name="parserOptions">Optional parser options.</param>
    /// <returns>A record reader over the UTF-8 encoded text.</returns>
    public static CsvRecordReader<byte, T> DeserializeRecords<T>(
        string data,
        out byte[] textBytes,
        CsvRecordOptions? recordOptions = null,
        CsvParserOptions? parserOptions = null)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(data);
        parserOptions ??= CsvParserOptions.Default;
        recordOptions ??= CsvRecordOptions.Default;

        textBytes = System.Text.Encoding.UTF8.GetBytes(data);
        var reader = ReadFromByteSpan(textBytes, parserOptions);
        var binder = CsvRecordBinderFactory.GetByteBinder<T>(recordOptions);
        return new CsvRecordReader<byte, T>(reader, binder, recordOptions.SkipRows,
            recordOptions.Progress, recordOptions.ProgressIntervalRows);
    }

    /// <summary>
    /// Deserializes CSV data from text into strongly typed records.
    /// Uses scalar parsing (no SIMD). For best performance, use the byte-based overload.
    /// </summary>
    /// <remarks>
    /// This method uses descriptor-based binding for backward compatibility.
    /// For optimal performance with SIMD acceleration, use <see cref="DeserializeRecords{T}(string, out byte[], CsvRecordOptions?, CsvParserOptions?)"/>.
    /// </remarks>
    public static CsvRecordReader<char, T> DeserializeRecords<T>(
        string data,
        CsvRecordOptions? recordOptions = null,
        CsvParserOptions? parserOptions = null)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(data);
        parserOptions ??= CsvParserOptions.Default;
        recordOptions ??= CsvRecordOptions.Default;

        var reader = ReadFromCharSpan(data.AsSpan(), parserOptions);
        var binder = CsvRecordBinderFactory.GetCharBinder<T>(recordOptions);
        return new CsvRecordReader<char, T>(reader, binder, recordOptions.SkipRows,
            recordOptions.Progress, recordOptions.ProgressIntervalRows);
    }

    /// <summary>
    /// Deserializes CSV data from UTF-8 bytes into strongly typed records.
    /// </summary>
    public static CsvRecordReader<byte, T> DeserializeRecordsFromBytes<T>(
        ReadOnlySpan<byte> data,
        CsvRecordOptions? recordOptions = null,
        CsvParserOptions? parserOptions = null)
        where T : class, new()
    {
        parserOptions ??= CsvParserOptions.Default;
        recordOptions ??= CsvRecordOptions.Default;

        var reader = ReadFromByteSpan(data, parserOptions);
        var binder = CsvRecordBinderFactory.GetByteBinder<T>(recordOptions);
        return new CsvRecordReader<byte, T>(reader, binder, recordOptions.SkipRows,
            recordOptions.Progress, recordOptions.ProgressIntervalRows);
    }

    /// <summary>
    /// Reads a CSV file as raw UTF-8 bytes for efficient parsing.
    /// </summary>
    /// <param name="path">Path to the CSV file.</param>
    /// <param name="fileBytes">The file bytes. Must be kept alive for the duration of parsing.</param>
    /// <param name="options">Optional parser options.</param>
    /// <returns>A row reader over the file bytes.</returns>
    /// <remarks>
    /// This method reads the file directly as UTF-8 bytes, avoiding the 2x memory overhead
    /// of UTF-16 string conversion. The returned byte array must be kept alive for the
    /// duration of parsing.
    ///
    /// For very large files that don't fit in memory, use streaming APIs instead.
    /// </remarks>
    public static CsvRowReader<byte> ReadFromFile(
        string path,
        out byte[] fileBytes,
        CsvParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        fileBytes = File.ReadAllBytes(path);
        return ReadFromByteSpan(fileBytes, options);
    }

    /// <summary>
    /// Reads a stream as UTF-8 bytes for efficient CSV parsing.
    /// </summary>
    /// <param name="stream">The stream containing UTF-8 CSV data.</param>
    /// <param name="streamBytes">The stream bytes. Must be kept alive for the duration of parsing.</param>
    /// <param name="options">Optional parser options.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <returns>A row reader over the stream bytes.</returns>
    /// <remarks>
    /// This method reads the entire stream into memory as UTF-8 bytes.
    /// For very large streams that don't fit in memory, use streaming APIs instead.
    /// </remarks>
    public static CsvRowReader<byte> ReadFromStream(
        Stream stream,
        out byte[] streamBytes,
        CsvParserOptions? options = null,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);

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

        return ReadFromByteSpan(streamBytes, options);
    }

    /// <summary>
    /// Creates an async streaming reader from a CSV file without loading the entire payload into memory.
    /// </summary>
    /// <param name="path">Filesystem path to the CSV file.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="CsvParserOptions.Default"/>.</param>
    /// <param name="bufferSize">Initial pooled buffer size in bytes.</param>
    /// <returns>A <see cref="CsvAsyncStreamReader"/> for asynchronous streaming.</returns>
    public static CsvAsyncStreamReader CreateAsyncStreamReader(
        string path,
        CsvParserOptions? options = null,
        int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= CsvParserOptions.Default;
        options.Validate();

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return new CsvAsyncStreamReader(stream, options, leaveOpen: false, initialBufferSize: bufferSize);
    }

    /// <summary>
    /// Creates an async streaming reader from a <see cref="Stream"/> without loading the entire payload into memory.
    /// </summary>
    /// <param name="stream">Readable stream containing UTF-8 CSV data.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="CsvParserOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the provided <paramref name="stream"/> remains open after parsing.</param>
    /// <param name="bufferSize">Initial pooled buffer size in bytes.</param>
    /// <returns>A <see cref="CsvAsyncStreamReader"/> for asynchronous streaming.</returns>
    public static CsvAsyncStreamReader CreateAsyncStreamReader(
        Stream stream,
        CsvParserOptions? options = null,
        bool leaveOpen = true,
        int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= CsvParserOptions.Default;
        options.Validate();

        return new CsvAsyncStreamReader(stream, options, leaveOpen, bufferSize);
    }
}
