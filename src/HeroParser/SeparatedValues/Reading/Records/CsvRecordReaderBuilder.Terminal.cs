using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using HeroParser.SeparatedValues.Reading.Binders;
using System.Text;

namespace HeroParser.SeparatedValues.Reading.Records;

public sealed partial class CsvRecordReaderBuilder<T>
{
    /// <summary>
    /// Reads records from a CSV string.
    /// Uses scalar parsing only (no SIMD acceleration).
    /// </summary>
    /// <remarks>
    /// <para>
    /// For optimal performance with SIMD acceleration, use the overload with the out parameter
    /// or use <see cref="FromFile(string, out byte[])"/> / <see cref="FromStream(Stream, out byte[], bool)"/>.
    /// </para>
    /// <para>
    /// When a fluent map has been configured via <see cref="WithMap"/> or <see cref="Map{TProperty}"/>,
    /// this returns a <c>char</c>-based reader using descriptor binding.
    /// </para>
    /// </remarks>
    public CsvRecordReader<char, T> FromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);

        if (mapSource is not null)
            return FromTextWithMap(csvText);

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
    /// <exception cref="NotSupportedException">
    /// Thrown when a fluent map is configured. Fluent maps use char-based descriptor binding;
    /// use <see cref="FromText(string)"/> instead.
    /// </exception>
    public CsvRecordReader<byte, T> FromText(string csvText, out byte[] textBytes)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        ThrowIfMapConfigured();
        var (parserOptions, recordOptions) = GetOptions();
        var reader = Csv.ReadFromText(csvText, out textBytes, parserOptions);
        var binder = CsvRecordBinderFactory.GetByteBinder<T>(recordOptions);
        return new CsvRecordReader<byte, T>(reader, binder, recordOptions.SkipRows,
            recordOptions.Progress, recordOptions.ProgressIntervalRows, recordOptions.ValidationMode,
            recordOptions.OnDeserializeError);
    }

    /// <summary>
    /// Reads records from a CSV file using the configured fluent map.
    /// The file is read as a string for char-based descriptor binding.
    /// </summary>
    /// <param name="path">Path to the CSV file.</param>
    /// <returns>A char-based record reader.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no fluent map is configured. Use <see cref="FromFile(string, out byte[])"/> for byte-based reading.
    /// </exception>
    public CsvRecordReader<char, T> FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfMapNotConfigured();
        var (parserOptions, _) = GetOptions();
        parserOptions.ValidateInputSize(new FileInfo(path).Length);
        _ = Csv.ReadFromFile(path, out var fileBytes, parserOptions);
        return FromTextWithMap(DecodeBufferedCsvBytes(fileBytes));
    }

    /// <summary>
    /// Reads records from a CSV file, parsing as raw UTF-8 bytes for efficiency.
    /// </summary>
    /// <param name="path">Path to the CSV file.</param>
    /// <param name="fileBytes">The file bytes. Must be kept alive for the duration of parsing.</param>
    /// <returns>A record reader over the file bytes.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when a fluent map is configured. Use <see cref="FromFile(string)"/> instead.
    /// </exception>
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
        ThrowIfMapConfigured();
        var (parserOptions, recordOptions) = GetOptions();
        parserOptions.ValidateInputSize(new FileInfo(path).Length);
        var rowReader = Csv.ReadFromFile(path, out fileBytes, parserOptions);
        var binder = CsvRecordBinderFactory.GetByteBinder<T>(recordOptions);
        return new CsvRecordReader<byte, T>(rowReader, binder, recordOptions.SkipRows,
            recordOptions.Progress, recordOptions.ProgressIntervalRows, recordOptions.ValidationMode,
            recordOptions.OnDeserializeError);
    }

    /// <summary>
    /// Reads records from a stream using the configured fluent map.
    /// The stream is read as a string for char-based descriptor binding.
    /// </summary>
    /// <param name="stream">The stream containing CSV data.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <returns>A char-based record reader.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no fluent map is configured. Use <see cref="FromStream(Stream, out byte[], bool)"/> for byte-based reading.
    /// </exception>
    public CsvRecordReader<char, T> FromStream(Stream stream, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ThrowIfMapNotConfigured();
        var (parserOptions, _) = GetOptions();
        if (stream.CanSeek)
            parserOptions.ValidateInputSize(stream.Length);
        _ = Csv.ReadFromStream(stream, out var streamBytes, parserOptions, leaveOpen);
        return FromTextWithMap(DecodeBufferedCsvBytes(streamBytes));
    }

    /// <summary>
    /// Reads records from a stream, parsing as raw UTF-8 bytes for efficiency.
    /// </summary>
    /// <param name="stream">The stream containing UTF-8 CSV data.</param>
    /// <param name="streamBytes">The stream bytes. Must be kept alive for the duration of parsing.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <returns>A record reader over the stream bytes.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when a fluent map is configured. Use <see cref="FromStream(Stream, bool)"/> instead.
    /// </exception>
    /// <remarks>
    /// This method reads the entire stream into memory as UTF-8 bytes, avoiding the 2x memory
    /// overhead of UTF-16 string conversion.
    ///
    /// For very large streams that don't fit in memory, use streaming APIs instead.
    /// </remarks>
    public CsvRecordReader<byte, T> FromStream(Stream stream, out byte[] streamBytes, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ThrowIfMapConfigured();
        var (parserOptions, recordOptions) = GetOptions();
        if (stream.CanSeek)
            parserOptions.ValidateInputSize(stream.Length);
        var rowReader = Csv.ReadFromStream(stream, out streamBytes, parserOptions, leaveOpen);
        var binder = CsvRecordBinderFactory.GetByteBinder<T>(recordOptions);
        return new CsvRecordReader<byte, T>(rowReader, binder, recordOptions.SkipRows,
            recordOptions.Progress, recordOptions.ProgressIntervalRows, recordOptions.ValidationMode,
            recordOptions.OnDeserializeError);
    }

    /// <summary>
    /// Asynchronously reads records from a CSV file without loading the entire file into memory.
    /// </summary>
    /// <param name="path">Filesystem path to the CSV file.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async sequence of deserialized records.</returns>
    /// <remarks>
    /// Unlike <see cref="FromFile(string, out byte[])"/>, this method does not load the entire file
    /// into memory. Use this for large files.
    /// </remarks>
    public async IAsyncEnumerable<T> FromFileAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfMapConfigured();
        ThrowIfOnErrorConfiguredForAsync();
        var (parserOptions, recordOptions) = GetOptions();
        parserOptions.ValidateInputSize(new FileInfo(path).Length);
        // Declare the FileStream with await using here (not in a finally) so CodeQL can prove
        // disposal across the iterator state machine and so it's released if a later
        // construction (e.g. PipeReader.Create) throws.
        var fileStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var fileStreamDisposal = fileStream.ConfigureAwait(false);
        var pipeReader = PipeReader.Create(fileStream);
        try
        {
            await foreach (var record in Csv.DeserializeRecordsAsync<T>(pipeReader, recordOptions, parserOptions, cancellationToken).ConfigureAwait(false))
                yield return record;
        }
        finally
        {
            await pipeReader.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously reads records from a stream without loading the entire stream into memory.
    /// </summary>
    /// <param name="stream">The stream containing UTF-8 CSV data.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream remains open after enumeration completes.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async sequence of deserialized records.</returns>
    /// <remarks>
    /// Unlike <see cref="FromStream(Stream, out byte[], bool)"/>, this method does not load the entire
    /// stream into memory. Use this for large streams.
    /// </remarks>
    public async IAsyncEnumerable<T> FromStreamAsync(
        Stream stream,
        bool leaveOpen = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ThrowIfMapConfigured();
        ThrowIfOnErrorConfiguredForAsync();
        var (parserOptions, recordOptions) = GetOptions();
        if (stream.CanSeek)
            parserOptions.ValidateInputSize(stream.Length);
        var pipeReader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: leaveOpen));
        try
        {
            await foreach (var record in Csv.DeserializeRecordsAsync<T>(pipeReader, recordOptions, parserOptions, cancellationToken).ConfigureAwait(false))
                yield return record;
        }
        finally
        {
            await pipeReader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private CsvRecordReader<char, T> FromTextWithMap(string csvText)
    {
        var (parserOptions, recordOptions) = GetOptions();
        var descriptor = mapSource!.BuildReadDescriptor();
        var binder = new CsvDescriptorBinder<T>(descriptor, recordOptions);
        var rowReader = Csv.ReadFromCharSpan(csvText.AsSpan(), parserOptions);
        return new CsvRecordReader<char, T>(rowReader, binder, recordOptions.SkipRows,
            recordOptions.Progress, recordOptions.ProgressIntervalRows, recordOptions.ValidationMode,
            recordOptions.OnDeserializeError);
    }

    private void ThrowIfMapConfigured()
    {
        if (mapSource is not null)
        {
            throw new NotSupportedException(
                "Byte-based overloads are not supported when a fluent map is configured. " +
                "Fluent maps use char-based descriptor binding. Use FromText(string), FromFile(string), " +
                "or FromStream(Stream, bool) instead.");
        }
    }

    private void ThrowIfOnErrorConfiguredForAsync()
    {
        if (onDeserializeError is not null)
        {
            throw new NotSupportedException(
                "OnError is not supported with FromFileAsync/FromStreamAsync. " +
                "The async pipe reader path does not support per-row error handling. " +
                "Use the synchronous FromFile/FromStream methods with OnError, or use " +
                "FromPipeReaderAsync without OnError.");
        }
    }

    private void ThrowIfMapNotConfigured()
    {
        if (mapSource is null)
        {
            throw new InvalidOperationException(
                "This overload requires a fluent map configured via WithMap() or Map(). " +
                "Use the byte-based overloads (with out parameter) for standard reading without a map.");
        }
    }

    private static string DecodeBufferedCsvBytes(byte[] data)
    {
        ReadOnlySpan<byte> bytes = data;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];

        return Encoding.UTF8.GetString(bytes);
    }
}
