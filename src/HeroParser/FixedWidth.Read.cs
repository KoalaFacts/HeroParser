using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records;
using HeroParser.FixedWidths.Records.Binding;

namespace HeroParser;

public static partial class FixedWidth
{
    /// <summary>
    /// Creates a reader that iterates over fixed-width records stored in a managed <see cref="string"/>.
    /// </summary>
    /// <param name="data">Complete fixed-width payload encoded as UTF-16.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="FixedWidthParserOptions.Default"/> is used.
    /// </param>
    /// <returns>A <see cref="FixedWidthCharSpanReader"/> that enumerates the parsed records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="FixedWidthException">Thrown when the payload violates the supplied <paramref name="options"/>.</exception>
    public static FixedWidthCharSpanReader ReadFromText(string data, FixedWidthParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ReadFromCharSpan(data.AsSpan(), options);
    }

    /// <summary>
    /// Creates a reader over a UTF-16 span (e.g., a substring or memory-mapped buffer).
    /// </summary>
    /// <param name="data">Span containing the fixed-width content.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="FixedWidthParserOptions.Default"/> is used.
    /// </param>
    /// <returns>A streaming reader that exposes each record as a <see cref="FixedWidthCharSpanRow"/>.</returns>
    /// <exception cref="FixedWidthException">Thrown when the input violates the supplied <paramref name="options"/>.</exception>
    public static FixedWidthCharSpanReader ReadFromCharSpan(ReadOnlySpan<char> data, FixedWidthParserOptions? options = null)
    {
        options ??= FixedWidthParserOptions.Default;
        options.Validate();
        return new FixedWidthCharSpanReader(data, options);
    }

    /// <summary>
    /// Creates a reader over a UTF-8 byte span.
    /// </summary>
    /// <param name="data">Span containing the fixed-width content encoded as UTF-8.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="FixedWidthParserOptions.Default"/> is used.
    /// </param>
    /// <returns>A streaming reader that exposes each record as a <see cref="FixedWidthCharSpanRow"/>.</returns>
    /// <remarks>
    /// This method decodes the UTF-8 bytes to UTF-16 before parsing.
    /// For large files, consider using <see cref="ReadFromStream"/> or <see cref="ReadFromFile"/> instead.
    /// </remarks>
    /// <exception cref="FixedWidthException">Thrown when the input violates the supplied <paramref name="options"/>.</exception>
    public static FixedWidthCharSpanReader ReadFromByteSpan(ReadOnlySpan<byte> data, FixedWidthParserOptions? options = null)
    {
        options ??= FixedWidthParserOptions.Default;
        options.Validate();

        // Decode UTF-8 to UTF-16
        var text = Encoding.UTF8.GetString(data);
        return new FixedWidthCharSpanReader(text.AsSpan(), options);
    }

    /// <summary>
    /// Creates a reader from a fixed-width file on disk using UTF-8 encoding by default.
    /// </summary>
    /// <param name="path">Filesystem path to the fixed-width file.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="FixedWidthParserOptions.Default"/> is used.
    /// </param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <returns>A <see cref="FixedWidthCharSpanReader"/> that reads the file contents.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="IOException">Propagates file system errors encountered while reading.</exception>
    public static FixedWidthCharSpanReader ReadFromFile(string path, FixedWidthParserOptions? options = null, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        encoding ??= Encoding.UTF8;
        options ??= FixedWidthParserOptions.Default;

        // Check file size before reading
        var fileInfo = new FileInfo(path);
        options.ValidateInputSize(fileInfo.Length);

        var text = File.ReadAllText(path, encoding);
        return ReadFromText(text, options);
    }

    /// <summary>
    /// Creates a reader from a <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">Readable stream containing fixed-width data.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="FixedWidthParserOptions.Default"/> is used.
    /// </param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the provided <paramref name="stream"/> remains open after reading.</param>
    /// <returns>A <see cref="FixedWidthCharSpanReader"/> that reads the stream contents.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    public static FixedWidthCharSpanReader ReadFromStream(Stream stream, FixedWidthParserOptions? options = null, Encoding? encoding = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        encoding ??= Encoding.UTF8;
        options ??= FixedWidthParserOptions.Default;

        // Check stream size before reading (if seekable)
        if (stream.CanSeek)
        {
            options.ValidateInputSize(stream.Length);
        }

        using var reader = new StreamReader(stream, encoding, leaveOpen: leaveOpen);
        var text = reader.ReadToEnd();
        return ReadFromText(text, options);
    }

    /// <summary>
    /// Asynchronously reads fixed-width data from a file on disk.
    /// </summary>
    /// <param name="path">Filesystem path to the fixed-width file.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="FixedWidthParserOptions.Default"/> is used.
    /// </param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="cancellationToken">Token to cancel I/O.</param>
    /// <returns>A <see cref="FixedWidthTextSource"/> that can produce a <see cref="FixedWidthCharSpanReader"/>.</returns>
    public static async Task<FixedWidthTextSource> ReadFromFileAsync(
        string path,
        FixedWidthParserOptions? options = null,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        encoding ??= Encoding.UTF8;
        options ??= FixedWidthParserOptions.Default;
        options.Validate();

        // Check file size before reading
        var fileInfo = new FileInfo(path);
        options.ValidateInputSize(fileInfo.Length);

        var text = await File.ReadAllTextAsync(path, encoding, cancellationToken).ConfigureAwait(false);
        return new FixedWidthTextSource(text, options);
    }

    /// <summary>
    /// Asynchronously reads fixed-width data from a stream.
    /// </summary>
    /// <param name="stream">Readable stream containing fixed-width data.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="FixedWidthParserOptions.Default"/> is used.
    /// </param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the provided <paramref name="stream"/> remains open after reading.</param>
    /// <param name="cancellationToken">Token to cancel I/O.</param>
    /// <returns>A <see cref="FixedWidthTextSource"/> that can produce a <see cref="FixedWidthCharSpanReader"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    public static async Task<FixedWidthTextSource> ReadFromStreamAsync(
        Stream stream,
        FixedWidthParserOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        encoding ??= Encoding.UTF8;
        options ??= FixedWidthParserOptions.Default;
        options.Validate();

        // Check stream size before reading (if seekable)
        if (stream.CanSeek)
        {
            options.ValidateInputSize(stream.Length);
        }

        using var reader = new StreamReader(stream, encoding, leaveOpen: leaveOpen);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return new FixedWidthTextSource(text, options);
    }

    /// <summary>
    /// Deserializes fixed-width data into strongly typed records.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize.</typeparam>
    /// <param name="data">The fixed-width content to parse.</param>
    /// <param name="options">Optional parser configuration.</param>
    /// <param name="culture">Culture for parsing values; defaults to <see cref="CultureInfo.InvariantCulture"/>.</param>
    /// <param name="onError">Optional error handler for deserialization errors.</param>
    /// <returns>A list of deserialized records.</returns>
    public static List<T> DeserializeRecords<T>(
        string data,
        FixedWidthParserOptions? options = null,
        CultureInfo? culture = null,
        FixedWidthDeserializeErrorHandler? onError = null)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= FixedWidthParserOptions.Default;
        culture ??= CultureInfo.InvariantCulture;

        var reader = ReadFromText(data, options);
        return FixedWidthRecordBinder<T>.Bind(reader, culture, onError);
    }

    /// <summary>
    /// Deserializes fixed-width data from a file into strongly typed records.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize.</typeparam>
    /// <param name="path">Filesystem path to the fixed-width file.</param>
    /// <param name="options">Optional parser configuration.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="culture">Culture for parsing values; defaults to <see cref="CultureInfo.InvariantCulture"/>.</param>
    /// <param name="onError">Optional error handler for deserialization errors.</param>
    /// <returns>A list of deserialized records.</returns>
    public static List<T> DeserializeRecords<T>(
        string path,
        FixedWidthParserOptions? options,
        Encoding? encoding,
        CultureInfo? culture = null,
        FixedWidthDeserializeErrorHandler? onError = null)
        where T : class, new()
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        encoding ??= Encoding.UTF8;
        culture ??= CultureInfo.InvariantCulture;

        var text = File.ReadAllText(path, encoding);
        return DeserializeRecords<T>(text, options, culture, onError);
    }

    /// <summary>
    /// Asynchronously deserializes fixed-width data from a file into strongly typed records.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize.</typeparam>
    /// <param name="path">Filesystem path to the fixed-width file.</param>
    /// <param name="options">Optional parser configuration.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="culture">Culture for parsing values; defaults to <see cref="CultureInfo.InvariantCulture"/>.</param>
    /// <param name="onError">Optional error handler for deserialization errors.</param>
    /// <param name="cancellationToken">Token to cancel I/O.</param>
    /// <returns>An async enumerable of deserialized records.</returns>
    public static async IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        string path,
        FixedWidthParserOptions? options = null,
        Encoding? encoding = null,
        CultureInfo? culture = null,
        FixedWidthDeserializeErrorHandler? onError = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, new()
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        encoding ??= Encoding.UTF8;
        culture ??= CultureInfo.InvariantCulture;

        var text = await File.ReadAllTextAsync(path, encoding, cancellationToken).ConfigureAwait(false);
        var records = DeserializeRecords<T>(text, options, culture, onError);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }

    /// <summary>
    /// Asynchronously deserializes fixed-width data from a stream into strongly typed records.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize.</typeparam>
    /// <param name="stream">Readable stream containing fixed-width data.</param>
    /// <param name="options">Optional parser configuration.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="culture">Culture for parsing values; defaults to <see cref="CultureInfo.InvariantCulture"/>.</param>
    /// <param name="onError">Optional error handler for deserialization errors.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the provided <paramref name="stream"/> remains open after reading.</param>
    /// <param name="cancellationToken">Token to cancel I/O.</param>
    /// <returns>An async enumerable of deserialized records.</returns>
    public static async IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        Stream stream,
        FixedWidthParserOptions? options = null,
        Encoding? encoding = null,
        CultureInfo? culture = null,
        FixedWidthDeserializeErrorHandler? onError = null,
        bool leaveOpen = true,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(stream);
        encoding ??= Encoding.UTF8;
        culture ??= CultureInfo.InvariantCulture;

        using var reader = new StreamReader(stream, encoding, leaveOpen: leaveOpen);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var records = DeserializeRecords<T>(text, options, culture, onError);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }
}

/// <summary>
/// Represents a buffered fixed-width text source for creating readers.
/// </summary>
public sealed class FixedWidthTextSource
{
    /// <summary>
    /// Gets the raw text content.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the parser options.
    /// </summary>
    public FixedWidthParserOptions Options { get; }

    internal FixedWidthTextSource(string text, FixedWidthParserOptions options)
    {
        Text = text;
        Options = options;
    }

    /// <summary>
    /// Creates a reader from the buffered text.
    /// </summary>
    /// <returns>A <see cref="FixedWidthCharSpanReader"/> for parsing the text.</returns>
    public FixedWidthCharSpanReader CreateReader()
        => FixedWidth.ReadFromCharSpan(Text.AsSpan(), Options);
}
