using System.Globalization;
using System.Text;

namespace HeroParser.FixedWidths.Writing;

/// <summary>
/// Fluent builder for configuring and executing fixed-width writing operations.
/// </summary>
/// <typeparam name="T">The record type to write.</typeparam>
public sealed class FixedWidthWriterBuilder<T>
{
    private string newLine = "\r\n";
    private char defaultPadChar = ' ';
    private FieldAlignment defaultAlignment = FieldAlignment.Left;
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private string nullValue = "";
    private string? dateTimeFormat;
    private string? dateOnlyFormat;
    private string? timeOnlyFormat;
    private string? numberFormat;
    private Encoding encoding = Encoding.UTF8;
    private int? maxRowCount;
    private OverflowBehavior overflowBehavior = OverflowBehavior.Truncate;
    private FixedWidthSerializeErrorHandler? onSerializeError;
    private long? maxOutputSize;

    // Cached options - invalidated when any setting changes
    private FixedWidthWriteOptions? cachedOptions;

    internal FixedWidthWriterBuilder() { }

    /// <summary>
    /// Sets the newline sequence for row endings.
    /// </summary>
    /// <param name="newLine">The newline sequence (typically "\r\n" or "\n").</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithNewLine(string newLine)
    {
        this.newLine = newLine;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the default padding character.
    /// </summary>
    /// <param name="padChar">The padding character.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithPadChar(char padChar)
    {
        defaultPadChar = padChar;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the default field alignment.
    /// </summary>
    /// <param name="alignment">The alignment.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithAlignment(FieldAlignment alignment)
    {
        defaultAlignment = alignment;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures left alignment (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> AlignLeft()
    {
        defaultAlignment = FieldAlignment.Left;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures right alignment.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> AlignRight()
    {
        defaultAlignment = FieldAlignment.Right;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the culture for formatting values.
    /// </summary>
    /// <param name="culture">The culture to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithCulture(CultureInfo culture)
    {
        this.culture = culture ?? CultureInfo.InvariantCulture;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the culture for formatting values using a culture name.
    /// </summary>
    /// <param name="cultureName">The culture name (e.g., "en-US", "de-DE").</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithCulture(string cultureName)
    {
        culture = CultureInfo.GetCultureInfo(cultureName);
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the string to write for null values.
    /// </summary>
    /// <param name="nullValue">The null representation.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithNullValue(string nullValue)
    {
        this.nullValue = nullValue ?? "";
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for DateTime values.
    /// </summary>
    /// <param name="format">The format string (e.g., "yyyyMMdd").</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithDateTimeFormat(string format)
    {
        dateTimeFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for DateOnly values.
    /// </summary>
    /// <param name="format">The format string (e.g., "yyyyMMdd").</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithDateOnlyFormat(string format)
    {
        dateOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for TimeOnly values.
    /// </summary>
    /// <param name="format">The format string (e.g., "HHmmss").</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithTimeOnlyFormat(string format)
    {
        timeOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for numeric values.
    /// </summary>
    /// <param name="format">The format string (e.g., "N2").</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithNumberFormat(string format)
    {
        numberFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the encoding for file output.
    /// </summary>
    /// <param name="encoding">The encoding to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithEncoding(Encoding encoding)
    {
        this.encoding = encoding ?? Encoding.UTF8;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of rows to write (DoS protection).
    /// </summary>
    /// <param name="maxRows">The maximum number of rows, or null for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithMaxRowCount(int? maxRows)
    {
        maxRowCount = maxRows;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the overflow behavior when values exceed field width.
    /// </summary>
    /// <param name="behavior">The overflow behavior.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithOverflowBehavior(OverflowBehavior behavior)
    {
        overflowBehavior = behavior;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures truncation for values that exceed field width (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> TruncateOnOverflow()
    {
        overflowBehavior = OverflowBehavior.Truncate;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures throwing an exception when values exceed field width.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> ThrowOnOverflow()
    {
        overflowBehavior = OverflowBehavior.Throw;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets a callback to handle serialization errors.
    /// </summary>
    /// <param name="handler">The error handler callback.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> OnError(FixedWidthSerializeErrorHandler handler)
    {
        onSerializeError = handler;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum total output size in characters (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum size, or null for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthWriterBuilder<T> WithMaxOutputSize(long? maxSize)
    {
        maxOutputSize = maxSize;
        cachedOptions = null;
        return this;
    }

    #region Terminal Methods

    /// <summary>
    /// Writes records to a string.
    /// </summary>
    /// <param name="records">The records to write.</param>
    /// <returns>The fixed-width content as a string.</returns>
    public string ToText(IEnumerable<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        using var stringWriter = new StringWriter();
        using var writer = new FixedWidthStreamWriter(stringWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records);
        writer.Flush();

        return stringWriter.ToString();
    }

    /// <summary>
    /// Asynchronously writes records to a string.
    /// </summary>
    /// <param name="records">The records to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fixed-width content as a string.</returns>
    public async ValueTask<string> ToTextAsync(IAsyncEnumerable<T> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        await using var stringWriter = new StringWriter();
        await using var writer = new FixedWidthStreamWriter(stringWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        return stringWriter.ToString();
    }

    /// <summary>
    /// Writes records to a file.
    /// </summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    public void ToFile(string path, IEnumerable<T> records)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var streamWriter = new StreamWriter(fileStream, encoding);
        using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records);
    }

    /// <summary>
    /// Writes records to a stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="leaveOpen">When true, leaves the stream open after writing.</param>
    public void ToStream(Stream stream, IEnumerable<T> records, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        using var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records);
    }

    /// <summary>
    /// Writes records to a TextWriter.
    /// </summary>
    /// <param name="textWriter">The TextWriter to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="leaveOpen">When true, leaves the writer open after writing.</param>
    public void ToWriter(TextWriter textWriter, IEnumerable<T> records, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(textWriter);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        using var writer = new FixedWidthStreamWriter(textWriter, options, leaveOpen);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records);
    }

    /// <summary>
    /// Asynchronously writes records to a file.
    /// </summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask ToFileAsync(string path, IAsyncEnumerable<T> records, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
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
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="leaveOpen">When true, leaves the stream open after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask ToStreamAsync(Stream stream, IAsyncEnumerable<T> records, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        await using var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        await using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes records from an IEnumerable to a file.
    /// </summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask ToFileAsync(string path, IEnumerable<T> records, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
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
    /// Asynchronously writes records from an IEnumerable to a stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="leaveOpen">When true, leaves the stream open after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask ToStreamAsync(Stream stream, IEnumerable<T> records, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        await using var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        await using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes records to a stream asynchronously with streaming semantics.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The async enumerable of records to write.</param>
    /// <param name="leaveOpen">When true, leaves the stream open after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ValueTask representing the asynchronous write operation.</returns>
    /// <remarks>
    /// This method streams records directly to the output without buffering all records first.
    /// Prefer this over <see cref="ToStreamAsync(Stream, IAsyncEnumerable{T}, bool, CancellationToken)"/>
    /// when working with large datasets or when immediate streaming is critical.
    /// </remarks>
    public async ValueTask ToStreamAsyncStreaming(Stream stream, IAsyncEnumerable<T> records, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        await using var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        await using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes records to a stream asynchronously with streaming semantics (IEnumerable overload).
    /// Avoids IAsyncEnumerable overhead for in-memory collections.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="leaveOpen">When true, leaves the stream open after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ValueTask representing the asynchronous write operation.</returns>
    public async ValueTask ToStreamAsyncStreaming(Stream stream, IEnumerable<T> records, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        await using var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        await using var writer = new FixedWidthStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = FixedWidthRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Private Helpers

    private FixedWidthWriteOptions GetOptions()
    {
        return cachedOptions ??= new FixedWidthWriteOptions
        {
            NewLine = newLine,
            DefaultPadChar = defaultPadChar,
            DefaultAlignment = defaultAlignment,
            Culture = culture,
            NullValue = nullValue,
            DateTimeFormat = dateTimeFormat,
            DateOnlyFormat = dateOnlyFormat,
            TimeOnlyFormat = timeOnlyFormat,
            NumberFormat = numberFormat,
            MaxRowCount = maxRowCount,
            OverflowBehavior = overflowBehavior,
            OnSerializeError = onSerializeError,
            MaxOutputSize = maxOutputSize
        };
    }

    #endregion
}

/// <summary>
/// Non-generic builder for manual row-by-row fixed-width writing.
/// </summary>
public sealed class FixedWidthWriterBuilder
{
    private string newLine = "\r\n";
    private char defaultPadChar = ' ';
    private FieldAlignment defaultAlignment = FieldAlignment.Left;
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private string nullValue = "";
    private string? dateTimeFormat;
    private string? dateOnlyFormat;
    private string? timeOnlyFormat;
    private string? numberFormat;
    private Encoding encoding = Encoding.UTF8;
    private OverflowBehavior overflowBehavior = OverflowBehavior.Truncate;
    private long? maxOutputSize;

    // Cached options - invalidated when any setting changes
    private FixedWidthWriteOptions? cachedOptions;

    internal FixedWidthWriterBuilder() { }

    /// <summary>
    /// Sets the newline sequence.
    /// </summary>
    public FixedWidthWriterBuilder WithNewLine(string newLine)
    {
        this.newLine = newLine;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the default padding character.
    /// </summary>
    public FixedWidthWriterBuilder WithPadChar(char padChar)
    {
        defaultPadChar = padChar;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the default field alignment.
    /// </summary>
    public FixedWidthWriterBuilder WithAlignment(FieldAlignment alignment)
    {
        defaultAlignment = alignment;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures left alignment (default).
    /// </summary>
    public FixedWidthWriterBuilder AlignLeft()
    {
        defaultAlignment = FieldAlignment.Left;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures right alignment.
    /// </summary>
    public FixedWidthWriterBuilder AlignRight()
    {
        defaultAlignment = FieldAlignment.Right;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the culture for formatting.
    /// </summary>
    public FixedWidthWriterBuilder WithCulture(CultureInfo culture)
    {
        this.culture = culture ?? CultureInfo.InvariantCulture;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the culture for formatting using a culture name.
    /// </summary>
    public FixedWidthWriterBuilder WithCulture(string cultureName)
    {
        culture = CultureInfo.GetCultureInfo(cultureName);
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the null value representation.
    /// </summary>
    public FixedWidthWriterBuilder WithNullValue(string nullValue)
    {
        this.nullValue = nullValue ?? "";
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the DateTime format string.
    /// </summary>
    public FixedWidthWriterBuilder WithDateTimeFormat(string format)
    {
        dateTimeFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the DateOnly format string.
    /// </summary>
    public FixedWidthWriterBuilder WithDateOnlyFormat(string format)
    {
        dateOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the TimeOnly format string.
    /// </summary>
    public FixedWidthWriterBuilder WithTimeOnlyFormat(string format)
    {
        timeOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for numeric values.
    /// </summary>
    public FixedWidthWriterBuilder WithNumberFormat(string format)
    {
        numberFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the encoding for file output.
    /// </summary>
    public FixedWidthWriterBuilder WithEncoding(Encoding encoding)
    {
        this.encoding = encoding ?? Encoding.UTF8;
        return this;
    }

    /// <summary>
    /// Sets the overflow behavior when values exceed field width.
    /// </summary>
    public FixedWidthWriterBuilder WithOverflowBehavior(OverflowBehavior behavior)
    {
        overflowBehavior = behavior;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures truncation for values that exceed field width (default).
    /// </summary>
    public FixedWidthWriterBuilder TruncateOnOverflow()
    {
        overflowBehavior = OverflowBehavior.Truncate;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures throwing an exception when values exceed field width.
    /// </summary>
    public FixedWidthWriterBuilder ThrowOnOverflow()
    {
        overflowBehavior = OverflowBehavior.Throw;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum total output size in characters (DoS protection).
    /// </summary>
    public FixedWidthWriterBuilder WithMaxOutputSize(long? maxSize)
    {
        maxOutputSize = maxSize;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Creates a writer for manual row-by-row writing.
    /// </summary>
    /// <param name="textWriter">The TextWriter to write to.</param>
    /// <param name="leaveOpen">When true, leaves the writer open.</param>
    /// <returns>A configured FixedWidthStreamWriter.</returns>
    public FixedWidthStreamWriter CreateWriter(TextWriter textWriter, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(textWriter);
        return new FixedWidthStreamWriter(textWriter, GetOptions(), leaveOpen);
    }

    /// <summary>
    /// Creates a writer for a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>A configured FixedWidthStreamWriter.</returns>
    public FixedWidthStreamWriter CreateFileWriter(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var streamWriter = new StreamWriter(stream, encoding);
        return new FixedWidthStreamWriter(streamWriter, GetOptions(), leaveOpen: false);
    }

    /// <summary>
    /// Creates a writer for a stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="leaveOpen">When true, leaves the stream open.</param>
    /// <returns>A configured FixedWidthStreamWriter.</returns>
    public FixedWidthStreamWriter CreateStreamWriter(Stream stream, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        return new FixedWidthStreamWriter(streamWriter, GetOptions(), leaveOpen: false);
    }

    private FixedWidthWriteOptions GetOptions()
    {
        return cachedOptions ??= new FixedWidthWriteOptions
        {
            NewLine = newLine,
            DefaultPadChar = defaultPadChar,
            DefaultAlignment = defaultAlignment,
            Culture = culture,
            NullValue = nullValue,
            DateTimeFormat = dateTimeFormat,
            DateOnlyFormat = dateOnlyFormat,
            TimeOnlyFormat = timeOnlyFormat,
            NumberFormat = numberFormat,
            OverflowBehavior = overflowBehavior,
            MaxOutputSize = maxOutputSize
        };
    }
}

