using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Streaming;
using HeroParser.FixedWidths.Writing;

namespace HeroParser;

public static partial class FixedWidth
{
    #region Fluent Builders

    /// <summary>
    /// Creates a typed writer builder for writing fixed-width records.
    /// </summary>
    /// <typeparam name="T">The record type to write. Must have properties decorated with <see cref="FixedWidths.Records.Binding.FixedWidthColumnAttribute"/>.</typeparam>
    /// <returns>A fluent builder for configuring the write operation.</returns>
    /// <example>
    /// <code>
    /// var text = FixedWidth.Write&lt;Person&gt;()
    ///     .WithPadChar(' ')
    ///     .AlignLeft()
    ///     .ToText(people);
    /// </code>
    /// </example>
    public static FixedWidthWriterBuilder<T> Write<T>() => new();

    /// <summary>
    /// Creates a non-generic writer builder for manual row-by-row fixed-width writing.
    /// </summary>
    /// <returns>A fluent builder for configuring the writer.</returns>
    /// <example>
    /// <code>
    /// using var writer = FixedWidth.Write()
    ///     .WithPadChar(' ')
    ///     .CreateFileWriter("output.txt");
    ///
    /// writer.WriteField("Name", 20);
    /// writer.WriteField("Age", 5, FieldAlignment.Right);
    /// writer.EndRow();
    /// </code>
    /// </example>
    public static FixedWidthWriterBuilder Write() => new();

    #endregion

    #region Direct Writing Methods

    /// <summary>
    /// Writes records to a string.
    /// </summary>
    /// <typeparam name="T">The record type to write.</typeparam>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer options.</param>
    /// <returns>The fixed-width content as a string.</returns>
    public static string WriteToText<T>(IEnumerable<T> records, FixedWidthWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        options ??= FixedWidthWriterOptions.Default;

        options.Validate();

        using var stringWriter = new StringWriter();
        using var writer = new FixedWidthStreamWriter(stringWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records);
        writer.Flush();

        return stringWriter.ToString();
    }

    /// <summary>
    /// Writes records to a file.
    /// </summary>
    /// <typeparam name="T">The record type to write.</typeparam>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    public static void WriteToFile<T>(
        string path,
        IEnumerable<T> records,
        FixedWidthWriterOptions? options = null,
        Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);
        options ??= FixedWidthWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        options.Validate();

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var streamWriter = new StreamWriter(fileStream, encoding);
        using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records);
    }

    /// <summary>
    /// Writes records to a stream.
    /// </summary>
    /// <typeparam name="T">The record type to write.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, leaves the stream open after writing.</param>
    public static void WriteToStream<T>(
        Stream stream,
        IEnumerable<T> records,
        FixedWidthWriterOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        options ??= FixedWidthWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        options.Validate();

        using var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records);
    }

    /// <summary>
    /// Asynchronously writes records to a file.
    /// </summary>
    /// <typeparam name="T">The record type to write.</typeparam>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask WriteToFileAsync<T>(
        string path,
        IEnumerable<T> records,
        FixedWidthWriterOptions? options = null,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);
        options ??= FixedWidthWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        options.Validate();

        await using var fileStream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await using var streamWriter = new StreamWriter(fileStream, encoding);
        await using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes records to a file.
    /// </summary>
    /// <typeparam name="T">The record type to write.</typeparam>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The async enumerable of records to write.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask WriteToFileAsync<T>(
        string path,
        IAsyncEnumerable<T> records,
        FixedWidthWriterOptions? options = null,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);
        options ??= FixedWidthWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        options.Validate();

        await using var fileStream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await using var streamWriter = new StreamWriter(fileStream, encoding);
        await using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes records to a stream.
    /// </summary>
    /// <typeparam name="T">The record type to write.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, leaves the stream open after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask WriteToStreamAsync<T>(
        Stream stream,
        IEnumerable<T> records,
        FixedWidthWriterOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        options ??= FixedWidthWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        options.Validate();

        await using var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        await using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes records to a stream.
    /// </summary>
    /// <typeparam name="T">The record type to write.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The async enumerable of records to write.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, leaves the stream open after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async ValueTask WriteToStreamAsync<T>(
        Stream stream,
        IAsyncEnumerable<T> records,
        FixedWidthWriterOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        options ??= FixedWidthWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        options.Validate();

        await using var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        await using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes records to fixed-width format (symmetric counterpart to DeserializeRecords).
    /// </summary>
    /// <typeparam name="T">The record type to serialize.</typeparam>
    /// <param name="records">The records to serialize.</param>
    /// <param name="options">Optional writer options.</param>
    /// <returns>The fixed-width content as a string.</returns>
    public static string SerializeRecords<T>(IEnumerable<T> records, FixedWidthWriterOptions? options = null)
        => WriteToText(records, options);

    /// <summary>
    /// Asynchronously writes records to fixed-width format and returns the result as a string.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="records">The async enumerable of records to write.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fixed-width content as a string.</returns>
    public static async ValueTask<string> WriteToTextAsync<T>(
        IAsyncEnumerable<T> records,
        FixedWidthWriterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        options ??= FixedWidthWriterOptions.Default;

        options.Validate();

        await using var stringWriter = new StringWriter();
        await using var writer = new FixedWidthStreamWriter(stringWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        return stringWriter.ToString();
    }

    #endregion

    #region Stream Writer Creation

    /// <summary>
    /// Creates a writer for manual row-by-row fixed-width writing.
    /// </summary>
    /// <param name="textWriter">The underlying text writer.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="leaveOpen">When true, leaves the text writer open on dispose.</param>
    /// <returns>A configured FixedWidthStreamWriter.</returns>
    /// <remarks>This method is an alias for <see cref="CreateStreamWriter(TextWriter, FixedWidthWriterOptions?, bool)"/> to match CSV API naming.</remarks>
    public static FixedWidthStreamWriter CreateWriter(
        TextWriter textWriter,
        FixedWidthWriterOptions? options = null,
        bool leaveOpen = false)
        => CreateStreamWriter(textWriter, options, leaveOpen);

    /// <summary>
    /// Creates a stream writer for manual row-by-row fixed-width writing.
    /// </summary>
    /// <param name="textWriter">The underlying text writer.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="leaveOpen">When true, leaves the text writer open on dispose.</param>
    /// <returns>A configured FixedWidthStreamWriter.</returns>
    public static FixedWidthStreamWriter CreateStreamWriter(
        TextWriter textWriter,
        FixedWidthWriterOptions? options = null,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(textWriter);
        options ??= FixedWidthWriterOptions.Default;
        options.Validate();

        return new FixedWidthStreamWriter(textWriter, options, leaveOpen);
    }

    /// <summary>
    /// Creates a stream writer for manual row-by-row fixed-width writing.
    /// </summary>
    /// <param name="stream">The underlying stream to write to.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, leaves the stream open on dispose.</param>
    /// <returns>A configured FixedWidthStreamWriter.</returns>
    public static FixedWidthStreamWriter CreateStreamWriter(
        Stream stream,
        FixedWidthWriterOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= FixedWidthWriterOptions.Default;
        options.Validate();
        encoding ??= Encoding.UTF8;

        var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        return new FixedWidthStreamWriter(streamWriter, options, leaveOpen: false);
    }

    /// <summary>
    /// Creates a file writer for manual row-by-row fixed-width writing.
    /// </summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <returns>A configured FixedWidthStreamWriter.</returns>
    public static FixedWidthStreamWriter CreateFileWriter(
        string path,
        FixedWidthWriterOptions? options = null,
        Encoding? encoding = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= FixedWidthWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        options.Validate();

        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var streamWriter = new StreamWriter(stream, encoding);
        return new FixedWidthStreamWriter(streamWriter, options, leaveOpen: false);
    }

    /// <summary>
    /// Creates an async stream writer for manual row-by-row fixed-width writing.
    /// </summary>
    /// <param name="stream">The underlying stream to write to.</param>
    /// <param name="options">Optional writer options.</param>
    /// <param name="encoding">Optional encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, leaves the stream open on dispose.</param>
    /// <returns>A configured FixedWidthAsyncStreamWriter.</returns>
    public static FixedWidthAsyncStreamWriter CreateAsyncStreamWriter(
        Stream stream,
        FixedWidthWriterOptions? options = null,
        Encoding? encoding = null,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= FixedWidthWriterOptions.Default;
        encoding ??= Encoding.UTF8;

        options.Validate();

        return new FixedWidthAsyncStreamWriter(stream, options, encoding, leaveOpen);
    }

    #endregion
}
