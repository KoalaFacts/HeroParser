using HeroParser.SeparatedValues;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser;

/// <summary>
/// Provides factory methods for creating high-performance CSV readers backed by SIMD parsing.
/// </summary>
/// <remarks>
/// The returned readers stream the source spans without allocating intermediate rows.
/// Call <c>Dispose</c> (or use a <c>using</c> statement) when you are finished to return pooled buffers.
/// </remarks>
public static class Csv
{
    /// <summary>
    /// Creates a reader that iterates over CSV records stored in a managed <see cref="string"/>.
    /// </summary>
    /// <param name="data">Complete CSV payload encoded as UTF-16.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <returns>A <see cref="CsvCharSpanReader"/> that enumerates the parsed rows.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="CsvException">Thrown when the payload violates the supplied <paramref name="options"/>.</exception>
    public static CsvCharSpanReader ReadFromText(string data, CsvParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ReadFromCharSpan(data.AsSpan(), options);
    }

    /// <summary>
    /// Creates a reader over a UTF-16 span (e.g., a substring or memory-mapped buffer).
    /// </summary>
    /// <param name="data">Span containing the CSV content.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <returns>A streaming reader that exposes each row as a <see cref="CsvCharSpanRow"/>.</returns>
    /// <exception cref="CsvException">Thrown when the input violates the supplied <paramref name="options"/>.</exception>
    public static CsvCharSpanReader ReadFromCharSpan(ReadOnlySpan<char> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();
        return new CsvCharSpanReader(data, options);
    }

    /// <summary>
    /// Creates a reader over UTF-8 encoded CSV data without transcoding to UTF-16.
    /// </summary>
    /// <param name="data">Span containing UTF-8 encoded CSV content.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <returns>A <see cref="CsvByteSpanReader"/> that yields UTF-8 backed rows.</returns>
    /// <exception cref="CsvException">Thrown when the payload violates the supplied <paramref name="options"/>.</exception>
    public static CsvByteSpanReader ReadFromByteSpan(ReadOnlySpan<byte> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();
        return new CsvByteSpanReader(data, options);
    }

    /// <summary>
    /// Creates a reader from a CSV file on disk using UTF-8 encoding by default.
    /// </summary>
    /// <param name="path">Filesystem path to the CSV file.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <returns>A <see cref="CsvCharSpanReader"/> that reads the file contents.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="IOException">Propagates file system errors encountered while reading.</exception>
    public static CsvStreamReader ReadFromFile(string path, CsvParserOptions? options = null, Encoding? encoding = null, int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        encoding ??= Encoding.UTF8;

        var stream = File.OpenRead(path);
        return ReadFromStream(stream, options, encoding, leaveOpen: false, bufferSize: bufferSize);
    }

    /// <summary>
    /// Creates a reader from a <see cref="Stream"/>, streaming without loading the full payload into memory.
    /// </summary>
    /// <param name="stream">Readable stream containing CSV data.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the provided <paramref name="stream"/> remains open after parsing.</param>
    /// <param name="bufferSize">Initial pooled buffer size in characters.</param>
    /// <returns>A streaming reader that enumerates rows from the stream contents.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    public static CsvStreamReader ReadFromStream(Stream stream, CsvParserOptions? options = null, Encoding? encoding = null, bool leaveOpen = true, int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        encoding ??= Encoding.UTF8;
        options ??= CsvParserOptions.Default;
        options.Validate();

        return new CsvStreamReader(stream, options, encoding, leaveOpen, bufferSize);
    }

    /// <summary>
    /// Asynchronously reads CSV data from a file on disk and returns a buffered text source.
    /// </summary>
    /// <param name="path">Filesystem path to the CSV file.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <param name="cancellationToken">Token to cancel I/O.</param>
    /// <returns>A <see cref="CsvTextSource"/> that can produce a <see cref="CsvCharSpanReader"/>.</returns>
    public static Task<CsvTextSource> ReadFromFileAsync(
        string path,
        CsvParserOptions? options = null,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        encoding ??= Encoding.UTF8;
        options ??= CsvParserOptions.Default;
        options.Validate();

        return ReadAsyncInternal(ct => File.ReadAllTextAsync(path, encoding, ct), options, cancellationToken);
    }

    /// <summary>
    /// Asynchronously reads CSV data from a stream and returns a buffered text source.
    /// </summary>
    /// <param name="stream">Readable stream containing CSV data.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the provided <paramref name="stream"/> remains open after parsing.</param>
    /// <param name="cancellationToken">Token to cancel I/O.</param>
    /// <returns>A <see cref="CsvTextSource"/> that can produce a <see cref="CsvCharSpanReader"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    public static Task<CsvTextSource> ReadFromStreamAsync(
        Stream stream,
        CsvParserOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        encoding ??= Encoding.UTF8;
        options ??= CsvParserOptions.Default;
        options.Validate();

        return ReadAsyncInternal(
            async ct =>
            {
                using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: leaveOpen);
                return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            },
            options,
            cancellationToken);
    }

    private static async Task<CsvTextSource> ReadAsyncInternal(
        Func<CancellationToken, Task<string>> readAsync,
        CsvParserOptions options,
        CancellationToken cancellationToken)
    {
        var text = await readAsync(cancellationToken).ConfigureAwait(false);
        return new CsvTextSource(text, options);
    }

    /// <summary>
    /// Creates an async streaming reader from a CSV file without loading the entire payload into memory.
    /// </summary>
    /// <param name="path">Filesystem path to the CSV file.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="CsvParserOptions.Default"/>.</param>
    /// <param name="encoding">Text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <param name="bufferSize">Initial pooled buffer size in characters.</param>
    /// <returns>A <see cref="CsvAsyncStreamReader"/> for asynchronous streaming.</returns>
    public static CsvAsyncStreamReader CreateAsyncStreamReader(
        string path,
        CsvParserOptions? options = null,
        Encoding? encoding = null,
        int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        encoding ??= Encoding.UTF8;
        options ??= CsvParserOptions.Default;
        options.Validate();

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return new CsvAsyncStreamReader(stream, options, encoding, leaveOpen: false, initialBufferSize: bufferSize);
    }

    /// <summary>
    /// Creates an async streaming reader from a <see cref="Stream"/> without loading the entire payload into memory.
    /// </summary>
    /// <param name="stream">Readable stream containing CSV data.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="CsvParserOptions.Default"/>.</param>
    /// <param name="encoding">Text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the provided <paramref name="stream"/> remains open after parsing.</param>
    /// <param name="bufferSize">Initial pooled buffer size in characters.</param>
    /// <returns>A <see cref="CsvAsyncStreamReader"/> for asynchronous streaming.</returns>
    public static CsvAsyncStreamReader CreateAsyncStreamReader(
        Stream stream,
        CsvParserOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true,
        int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        encoding ??= Encoding.UTF8;
        options ??= CsvParserOptions.Default;
        options.Validate();

        return new CsvAsyncStreamReader(stream, options, encoding, leaveOpen, initialBufferSize: bufferSize);
    }
}
