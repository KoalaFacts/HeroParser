using HeroParser.SeparatedValues.Writing;
using HeroParser.SeparatedValues.Streaming;
using System.Text;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Creates a fluent builder for writing CSV records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record type to serialize.</typeparam>
    /// <returns>A <see cref="CsvWriterBuilder{T}"/> for configuring and executing the write operation.</returns>
    /// <example>
    /// <code>
    /// var csv = Csv.Write&lt;Person&gt;()
    ///     .WithDelimiter(';')
    ///     .WithHeader()
    ///     .ToText(records);
    /// </code>
    /// </example>
    public static CsvWriterBuilder<T> Write<T>() => new();

    /// <summary>
    /// Creates a fluent builder for manual row-by-row CSV writing.
    /// </summary>
    /// <returns>A <see cref="CsvWriterBuilder"/> for configuring and creating a low-level CSV writer.</returns>
    /// <example>
    /// <code>
    /// // Manual row-by-row writing
    /// using var writer = Csv.Write()
    ///     .WithDelimiter(';')
    ///     .CreateWriter(textWriter);
    ///
    /// writer.WriteField("Name");
    /// writer.WriteField("Age");
    /// writer.EndRow();
    /// </code>
    /// </example>
    public static CsvWriterBuilder Write() => new();

    /// <summary>
    /// Creates a low-level CSV writer with default options.
    /// </summary>
    /// <param name="writer">The TextWriter to write to.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="leaveOpen">When true, the writer is not disposed when the CsvStreamWriter is disposed.</param>
    /// <returns>A <see cref="CsvStreamWriter"/> for writing CSV content.</returns>
    public static CsvStreamWriter CreateWriter(TextWriter writer, CsvWriterOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return new CsvStreamWriter(writer, options, leaveOpen);
    }

    /// <summary>
    /// Creates a CSV writer that writes to a file.
    /// </summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <returns>A <see cref="CsvStreamWriter"/> for writing CSV content.</returns>
    public static CsvStreamWriter CreateFileWriter(string path, CsvWriterOptions? options = null, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        encoding ??= Encoding.UTF8;

        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var textWriter = new StreamWriter(stream, encoding);
        return new CsvStreamWriter(textWriter, options, leaveOpen: false);
    }

    /// <summary>
    /// Creates a CSV writer that writes to a stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, the stream remains open after the writer is disposed.</param>
    /// <returns>A <see cref="CsvStreamWriter"/> for writing CSV content.</returns>
    public static CsvStreamWriter CreateStreamWriter(Stream stream, CsvWriterOptions? options = null, Encoding? encoding = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        encoding ??= Encoding.UTF8;

        var textWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        return new CsvStreamWriter(textWriter, options, leaveOpen: true);
    }

    /// <summary>
    /// Writes records to CSV format and returns the result as a string.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <returns>The CSV content as a string.</returns>
    public static string WriteToText<T>(IEnumerable<T> records, CsvWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        options ??= CsvWriterOptions.Default;

        using var stringWriter = new StringWriter();
        using var writer = new CsvStreamWriter(stringWriter, options);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records, options.WriteHeader);
        writer.Flush();

        return stringWriter.ToString();
    }

    /// <summary>
    /// Serializes records to CSV format and returns the result as a string.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="records">The records to serialize.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <returns>The CSV content as a string.</returns>
    /// <remarks>
    /// This is the symmetric counterpart to <see cref="DeserializeRecords{T}(string, SeparatedValues.Records.CsvRecordOptions?, SeparatedValues.CsvParserOptions?)"/>.
    /// </remarks>
    public static string SerializeRecords<T>(IEnumerable<T> records, CsvWriterOptions? options = null)
        => WriteToText(records, options);

    /// <summary>
    /// Asynchronously writes records to a stream using IAsyncEnumerable.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, the stream remains open after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask WriteToStreamAsync<T>(
        Stream stream,
        IAsyncEnumerable<T> records,
        CsvWriterOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        options ??= CsvWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        await using var writer = new CsvAsyncStreamWriter(stream, options, encoding, leaveOpen);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, options.WriteHeader, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes records to a stream using IEnumerable (avoids IAsyncEnumerable overhead for in-memory collections).
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, the stream remains open after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask WriteToStreamAsync<T>(
        Stream stream,
        IEnumerable<T> records,
        CsvWriterOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        options ??= CsvWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        await using var writer = new CsvAsyncStreamWriter(stream, options, encoding, leaveOpen);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, options.WriteHeader, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes records to a file.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    public static void WriteToFile<T>(string path, IEnumerable<T> records, CsvWriterOptions? options = null, Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);
        options ??= CsvWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var textWriter = new StreamWriter(stream, encoding);
        using var writer = new CsvStreamWriter(textWriter, options);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records, options.WriteHeader);
    }

    /// <summary>
    /// Asynchronously writes records to a file using IAsyncEnumerable.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask WriteToFileAsync<T>(
        string path,
        IAsyncEnumerable<T> records,
        CsvWriterOptions? options = null,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);
        options ??= CsvWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using var writer = new CsvAsyncStreamWriter(stream, options, encoding, leaveOpen: false);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, options.WriteHeader, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes records to a file using IEnumerable (avoids IAsyncEnumerable overhead for in-memory collections).
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask WriteToFileAsync<T>(
        string path,
        IEnumerable<T> records,
        CsvWriterOptions? options = null,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);
        options ??= CsvWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using var writer = new CsvAsyncStreamWriter(stream, options, encoding, leaveOpen: false);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, options.WriteHeader, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a streaming async CSV writer for direct stream writing.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, the stream remains open after the writer is disposed.</param>
    /// <returns>A <see cref="CsvAsyncStreamWriter"/> for async CSV writing.</returns>
    public static CsvAsyncStreamWriter CreateAsyncStreamWriter(
        Stream stream,
        CsvWriterOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new CsvAsyncStreamWriter(stream, options, encoding, leaveOpen);
    }

    /// <summary>
    /// Writes records to a stream.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, the stream remains open after writing.</param>
    public static void WriteToStream<T>(Stream stream, IEnumerable<T> records, CsvWriterOptions? options = null, Encoding? encoding = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        options ??= CsvWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        using var textWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        using var writer = new CsvStreamWriter(textWriter, options);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records, options.WriteHeader);
    }
}
