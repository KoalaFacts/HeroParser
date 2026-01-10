using System.Buffers;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser;

public static partial class Csv
{
    private const int DEFAULT_MAX_BUFFERED_STREAM_BYTES = 128 * 1024 * 1024;
    private const int DEFAULT_STREAM_COPY_BUFFER_SIZE = 16 * 1024;

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
    public static CsvRowReader<byte> ReadFromText(string data, out byte[] textBytes, CsvReadOptions? options = null)
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
    /// or use <see cref="ReadFromFile"/> /
    /// <see cref="ReadFromStream(Stream, out byte[], CsvReadOptions?, bool)"/>.
    /// </remarks>
    public static CsvRowReader<char> ReadFromText(string data, CsvReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ReadFromCharSpan(data.AsSpan(), options);
    }

    /// <summary>
    /// Creates a reader over a UTF-16 span.
    /// </summary>
    public static CsvRowReader<char> ReadFromCharSpan(ReadOnlySpan<char> data, CsvReadOptions? options = null)
    {
        options ??= CsvReadOptions.Default;
        options.Validate();
        return new CsvRowReader<char>(data, options);
    }

    /// <summary>
    /// Creates a reader over UTF-8 encoded CSV data.
    /// </summary>
    public static CsvRowReader<byte> ReadFromByteSpan(ReadOnlySpan<byte> data, CsvReadOptions? options = null)
    {
        options ??= CsvReadOptions.Default;
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
        CsvReadOptions? parserOptions = null)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(data);
        parserOptions ??= CsvReadOptions.Default;
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
    /// For optimal performance with SIMD acceleration, use <see cref="DeserializeRecords{T}(string, out byte[], CsvRecordOptions?, CsvReadOptions?)"/>.
    /// </remarks>
    public static CsvRecordReader<char, T> DeserializeRecords<T>(
        string data,
        CsvRecordOptions? recordOptions = null,
        CsvReadOptions? parserOptions = null)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(data);
        parserOptions ??= CsvReadOptions.Default;
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
        CsvReadOptions? parserOptions = null)
        where T : class, new()
    {
        parserOptions ??= CsvReadOptions.Default;
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
        CsvReadOptions? options = null)
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
    /// Streams larger than 128 MB will throw to avoid excessive buffering; use streaming APIs instead.
    /// </remarks>
    public static CsvRowReader<byte> ReadFromStream(
        Stream stream,
        out byte[] streamBytes,
        CsvReadOptions? options = null,
        bool leaveOpen = true)
    {
        return ReadFromStream(stream, out streamBytes, options, leaveOpen, DEFAULT_MAX_BUFFERED_STREAM_BYTES);
    }

    /// <summary>
    /// Reads a stream as UTF-8 bytes for efficient CSV parsing.
    /// </summary>
    /// <param name="stream">The stream containing UTF-8 CSV data.</param>
    /// <param name="streamBytes">The stream bytes. Must be kept alive for the duration of parsing.</param>
    /// <param name="options">Optional parser options.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <param name="maxBytesToBuffer">Maximum bytes to buffer into memory before throwing.</param>
    /// <returns>A row reader over the stream bytes.</returns>
    /// <remarks>
    /// This method reads the entire stream into memory as UTF-8 bytes.
    /// For large streams, prefer <see cref="CreateAsyncStreamReader(Stream, CsvReadOptions?, bool, int)"/>.
    /// </remarks>
    public static CsvRowReader<byte> ReadFromStream(
        Stream stream,
        out byte[] streamBytes,
        CsvReadOptions? options,
        bool leaveOpen,
        int maxBytesToBuffer)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxBytesToBuffer <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytesToBuffer),
                maxBytesToBuffer,
                "Max bytes to buffer must be positive.");
        }

        if (stream is MemoryStream ms)
        {
            long remaining = ms.Length - ms.Position;
            if (remaining > maxBytesToBuffer)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Stream length exceeds maximum buffered size of {maxBytesToBuffer:N0} bytes. " +
                    "Use Csv.CreateAsyncStreamReader for large inputs.");
            }

            if (ms.Position == 0)
            {
                if (ms.TryGetBuffer(out var buffer) &&
                    buffer.Array is not null &&
                    buffer.Offset == 0 &&
                    buffer.Count == ms.Length &&
                    buffer.Array.Length == buffer.Count)
                {
                    streamBytes = buffer.Array;
                    if (!leaveOpen)
                        stream.Dispose();
                    return ReadFromByteSpan(streamBytes, options);
                }

                streamBytes = ms.ToArray();
                if (!leaveOpen)
                    stream.Dispose();
                return ReadFromByteSpan(streamBytes, options);
            }
        }
        else if (stream.CanSeek)
        {
            long remaining = stream.Length - stream.Position;
            if (remaining > maxBytesToBuffer)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Stream length exceeds maximum buffered size of {maxBytesToBuffer:N0} bytes. " +
                    "Use Csv.CreateAsyncStreamReader for large inputs.");
            }
        }

        streamBytes = ReadAllBytes(stream, maxBytesToBuffer);

        if (!leaveOpen)
            stream.Dispose();

        return ReadFromByteSpan(streamBytes, options);
    }

    private static byte[] ReadAllBytes(Stream stream, int maxBytesToBuffer)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(DEFAULT_STREAM_COPY_BUFFER_SIZE);
        try
        {
            int initialCapacity = 0;
            if (stream.CanSeek)
            {
                long remaining = stream.Length - stream.Position;
                if (remaining > 0)
                    initialCapacity = (int)Math.Min(remaining, maxBytesToBuffer);
            }

            using var memoryStream = initialCapacity > 0
                ? new MemoryStream(initialCapacity)
                : new MemoryStream();
            long total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxBytesToBuffer)
                {
                    throw new CsvException(
                        CsvErrorCode.ParseError,
                        $"Stream length exceeds maximum buffered size of {maxBytesToBuffer:N0} bytes. " +
                        "Use Csv.CreateAsyncStreamReader for large inputs.");
                }
                memoryStream.Write(buffer, 0, read);
            }

            return memoryStream.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    /// <summary>
    /// Creates an async streaming reader from a CSV file without loading the entire payload into memory.
    /// </summary>
    /// <param name="path">Filesystem path to the CSV file.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="CsvReadOptions.Default"/>.</param>
    /// <param name="bufferSize">Initial pooled buffer size in bytes.</param>
    /// <returns>A <see cref="CsvAsyncStreamReader"/> for asynchronous streaming.</returns>
    public static CsvAsyncStreamReader CreateAsyncStreamReader(
        string path,
        CsvReadOptions? options = null,
        int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= CsvReadOptions.Default;
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
    /// <param name="options">Parser configuration; defaults to <see cref="CsvReadOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the provided <paramref name="stream"/> remains open after parsing.</param>
    /// <param name="bufferSize">Initial pooled buffer size in bytes.</param>
    /// <returns>A <see cref="CsvAsyncStreamReader"/> for asynchronous streaming.</returns>
    public static CsvAsyncStreamReader CreateAsyncStreamReader(
        Stream stream,
        CsvReadOptions? options = null,
        bool leaveOpen = true,
        int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= CsvReadOptions.Default;
        options.Validate();

        return new CsvAsyncStreamReader(stream, options, leaveOpen, bufferSize);
    }
}

