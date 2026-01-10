using System.Globalization;
using System.Text;

namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// Fluent builder for configuring and executing CSV writing operations.
/// </summary>
/// <typeparam name="T">The record type to write.</typeparam>
public sealed class CsvWriterBuilder<T>
{
    private char delimiter = ',';
    private char quote = '"';
    private string newLine = "\r\n";
    private QuoteStyle quoteStyle = QuoteStyle.WhenNeeded;
    private bool writeHeader = true;
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private string nullValue = "";
    private string? dateTimeFormat;
    private string? dateOnlyFormat;
    private string? timeOnlyFormat;
    private string? numberFormat;
    private Encoding encoding = Encoding.UTF8;
    private int? maxRowCount;
    private CsvSerializeErrorHandler? onSerializeError;

    // Security and DoS protection
    private CsvInjectionProtection injectionProtection = CsvInjectionProtection.None;
    private IReadOnlySet<char>? additionalDangerousChars;
    private long? maxOutputSize;
    private int? maxFieldSize;
    private int? maxColumnCount;

    // Cached options - invalidated when any setting changes
    private CsvWriteOptions? cachedOptions;

    internal CsvWriterBuilder() { }

    /// <summary>
    /// Sets the field delimiter character.
    /// </summary>
    /// <param name="delimiter">The delimiter character (must be ASCII).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithDelimiter(char delimiter)
    {
        this.delimiter = delimiter;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the quote character used for escaping.
    /// </summary>
    /// <param name="quote">The quote character (must be ASCII).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithQuote(char quote)
    {
        this.quote = quote;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the newline sequence for row endings.
    /// </summary>
    /// <param name="newLine">The newline sequence (typically "\r\n" or "\n").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithNewLine(string newLine)
    {
        this.newLine = newLine;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures quoting to always quote all fields.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> AlwaysQuote()
    {
        quoteStyle = QuoteStyle.Always;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures quoting to never quote fields. Use with caution.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> NeverQuote()
    {
        quoteStyle = QuoteStyle.Never;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures quoting to quote only when necessary (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> QuoteWhenNeeded()
    {
        quoteStyle = QuoteStyle.WhenNeeded;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Enables writing a header row with property names.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithHeader()
    {
        writeHeader = true;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Disables writing a header row.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithoutHeader()
    {
        writeHeader = false;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the culture for formatting values.
    /// </summary>
    /// <param name="culture">The culture to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithCulture(CultureInfo culture)
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
    public CsvWriterBuilder<T> WithCulture(string cultureName)
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
    public CsvWriterBuilder<T> WithNullValue(string nullValue)
    {
        this.nullValue = nullValue ?? "";
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for DateTime values.
    /// </summary>
    /// <param name="format">The format string (e.g., "yyyy-MM-dd HH:mm:ss").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithDateTimeFormat(string format)
    {
        dateTimeFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for DateOnly values.
    /// </summary>
    /// <param name="format">The format string (e.g., "yyyy-MM-dd").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithDateOnlyFormat(string format)
    {
        dateOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for TimeOnly values.
    /// </summary>
    /// <param name="format">The format string (e.g., "HH:mm:ss").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithTimeOnlyFormat(string format)
    {
        timeOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for numeric values.
    /// </summary>
    /// <param name="format">The format string (e.g., "N2", "F4", "C").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithNumberFormat(string format)
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
    /// <remarks>
    /// Encoding is used only for file/stream output and is not part of <see cref="CsvWriteOptions"/>.
    /// </remarks>
    public CsvWriterBuilder<T> WithEncoding(Encoding encoding)
    {
        this.encoding = encoding ?? Encoding.UTF8;
        // Note: encoding is not part of CsvWriteOptions, so no need to invalidate cachedOptions
        return this;
    }

    /// <summary>
    /// Sets the maximum number of rows to write (DoS protection).
    /// </summary>
    /// <param name="maxRows">The maximum number of rows, or null for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithMaxRowCount(int? maxRows)
    {
        maxRowCount = maxRows;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets a callback to handle serialization errors.
    /// </summary>
    /// <param name="handler">The error handler callback.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> OnError(CsvSerializeErrorHandler handler)
    {
        onSerializeError = handler;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Enables CSV injection protection with the specified mode.
    /// </summary>
    /// <param name="mode">The protection mode (default: EscapeWithQuote).</param>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// CSV injection occurs when user-supplied data begins with characters like <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>,
    /// tab, or carriage return that spreadsheet applications interpret as formulas.
    /// </remarks>
    public CsvWriterBuilder<T> WithInjectionProtection(CsvInjectionProtection mode = CsvInjectionProtection.EscapeWithQuote)
    {
        injectionProtection = mode;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Adds additional characters to treat as dangerous for injection protection.
    /// </summary>
    /// <param name="chars">Additional dangerous characters.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithDangerousChars(params char[] chars)
    {
        additionalDangerousChars = chars.ToHashSet();
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum total output size in characters (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum size, or null for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithMaxOutputSize(long? maxSize)
    {
        maxOutputSize = maxSize;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum size for a single field in characters (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum size, or null for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithMaxFieldSize(int? maxSize)
    {
        maxFieldSize = maxSize;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of columns per row (DoS protection).
    /// </summary>
    /// <param name="maxColumns">The maximum columns, or null for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder<T> WithMaxColumnCount(int? maxColumns)
    {
        maxColumnCount = maxColumns;
        cachedOptions = null;
        return this;
    }

    #region Terminal Methods

    /// <summary>
    /// Writes records to a string.
    /// </summary>
    /// <param name="records">The records to write.</param>
    /// <returns>The CSV content as a string.</returns>
    public string ToText(IEnumerable<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        using var stringWriter = new StringWriter();
        using var writer = new CsvStreamWriter(stringWriter, options, leaveOpen: true);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records, writeHeader);
        writer.Flush();

        return stringWriter.ToString();
    }

    /// <summary>
    /// Asynchronously writes records to a string.
    /// </summary>
    /// <param name="records">The records to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The CSV content as a string.</returns>
    public async ValueTask<string> ToTextAsync(IAsyncEnumerable<T> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        await using var stringWriter = new StringWriter();
        await using var writer = new CsvStreamWriter(stringWriter, options, leaveOpen: true);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, writeHeader, cancellationToken).ConfigureAwait(false);
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
        using var writer = new CsvStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records, writeHeader);
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
        using var writer = new CsvStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records, writeHeader);
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
        using var writer = new CsvStreamWriter(textWriter, options, leaveOpen);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        recordWriter.WriteRecords(writer, records, writeHeader);
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
        await using var writer = new CsvStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, writeHeader, cancellationToken).ConfigureAwait(false);
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
        await using var writer = new CsvStreamWriter(streamWriter, options, leaveOpen: true);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);
        await recordWriter.WriteRecordsAsync(writer, records, writeHeader, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes records directly to a stream using the streaming async writer.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="leaveOpen">When true, leaves the stream open after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ValueTask representing the asynchronous write operation.</returns>
    /// <remarks>
    /// This method uses <see cref="CsvAsyncStreamWriter"/> for truly non-blocking I/O.
    /// Prefer this over <see cref="ToStreamAsync"/> for large datasets or when streaming is critical.
    /// </remarks>
    public async ValueTask ToStreamAsyncStreaming(Stream stream, IAsyncEnumerable<T> records, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        await using var writer = new CsvAsyncStreamWriter(stream, options, encoding, leaveOpen);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);

        // Use the proper async method with compiled accessors (no reflection)
        await recordWriter.WriteRecordsAsync(writer, records, writeHeader && options.WriteHeader, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes records to a stream asynchronously using the true async writer (IEnumerable overload).
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
        await using var writer = new CsvAsyncStreamWriter(stream, options, encoding, leaveOpen);
        var recordWriter = CsvRecordWriterFactory.GetWriter<T>(options);

        // Use the proper async method with compiled accessors (no reflection)
        await recordWriter.WriteRecordsAsync(writer, records, writeHeader && options.WriteHeader, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Private Helpers

    private CsvWriteOptions GetOptions()
    {
        return cachedOptions ??= new CsvWriteOptions
        {
            Delimiter = delimiter,
            Quote = quote,
            NewLine = newLine,
            QuoteStyle = quoteStyle,
            WriteHeader = writeHeader,
            Culture = culture,
            NullValue = nullValue,
            DateTimeFormat = dateTimeFormat,
            DateOnlyFormat = dateOnlyFormat,
            TimeOnlyFormat = timeOnlyFormat,
            NumberFormat = numberFormat,
            MaxRowCount = maxRowCount,
            OnSerializeError = onSerializeError,
            InjectionProtection = injectionProtection,
            AdditionalDangerousChars = additionalDangerousChars,
            MaxOutputSize = maxOutputSize,
            MaxFieldSize = maxFieldSize,
            MaxColumnCount = maxColumnCount
        };
    }

    #endregion
}

/// <summary>
/// Non-generic builder for manual row-by-row writing.
/// </summary>
public sealed class CsvWriterBuilder
{
    private char delimiter = ',';
    private char quote = '"';
    private string newLine = "\r\n";
    private QuoteStyle quoteStyle = QuoteStyle.WhenNeeded;
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private string nullValue = "";
    private string? dateTimeFormat;
    private string? dateOnlyFormat;
    private string? timeOnlyFormat;
    private string? numberFormat;
    private Encoding encoding = Encoding.UTF8;

    // Security and DoS protection
    private CsvInjectionProtection injectionProtection = CsvInjectionProtection.None;
    private IReadOnlySet<char>? additionalDangerousChars;
    private long? maxOutputSize;
    private int? maxFieldSize;
    private int? maxColumnCount;

    // Cached options - invalidated when any setting changes
    private CsvWriteOptions? cachedOptions;

    internal CsvWriterBuilder() { }

    /// <summary>
    /// Sets the field delimiter character.
    /// </summary>
    public CsvWriterBuilder WithDelimiter(char delimiter)
    {
        this.delimiter = delimiter;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the quote character.
    /// </summary>
    public CsvWriterBuilder WithQuote(char quote)
    {
        this.quote = quote;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the newline sequence.
    /// </summary>
    public CsvWriterBuilder WithNewLine(string newLine)
    {
        this.newLine = newLine;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures quoting to always quote all fields.
    /// </summary>
    public CsvWriterBuilder AlwaysQuote()
    {
        quoteStyle = QuoteStyle.Always;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures quoting to never quote fields.
    /// </summary>
    public CsvWriterBuilder NeverQuote()
    {
        quoteStyle = QuoteStyle.Never;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Configures quoting to quote only when necessary (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder QuoteWhenNeeded()
    {
        quoteStyle = QuoteStyle.WhenNeeded;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the culture for formatting.
    /// </summary>
    public CsvWriterBuilder WithCulture(CultureInfo culture)
    {
        this.culture = culture ?? CultureInfo.InvariantCulture;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the culture for formatting using a culture name.
    /// </summary>
    /// <param name="cultureName">The culture name (e.g., "en-US", "de-DE").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder WithCulture(string cultureName)
    {
        culture = CultureInfo.GetCultureInfo(cultureName);
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the null value representation.
    /// </summary>
    public CsvWriterBuilder WithNullValue(string nullValue)
    {
        this.nullValue = nullValue ?? "";
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the DateTime format string.
    /// </summary>
    public CsvWriterBuilder WithDateTimeFormat(string format)
    {
        dateTimeFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the DateOnly format string.
    /// </summary>
    /// <param name="format">The format string (e.g., "yyyy-MM-dd").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder WithDateOnlyFormat(string format)
    {
        dateOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the TimeOnly format string.
    /// </summary>
    /// <param name="format">The format string (e.g., "HH:mm:ss").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder WithTimeOnlyFormat(string format)
    {
        timeOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for numeric values.
    /// </summary>
    /// <param name="format">The format string (e.g., "N2", "F4", "C").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder WithNumberFormat(string format)
    {
        numberFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the encoding for file output.
    /// </summary>
    /// <remarks>
    /// Encoding is used only for file/stream output and is not part of <see cref="CsvWriteOptions"/>.
    /// </remarks>
    public CsvWriterBuilder WithEncoding(Encoding encoding)
    {
        this.encoding = encoding ?? Encoding.UTF8;
        // Note: encoding is not part of CsvWriteOptions, so no need to invalidate cachedOptions
        return this;
    }

    /// <summary>
    /// Enables CSV injection protection with the specified mode.
    /// </summary>
    /// <param name="mode">The protection mode (default: EscapeWithQuote).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder WithInjectionProtection(CsvInjectionProtection mode = CsvInjectionProtection.EscapeWithQuote)
    {
        injectionProtection = mode;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Adds additional characters to treat as dangerous for injection protection.
    /// </summary>
    /// <param name="chars">Additional dangerous characters.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder WithDangerousChars(params char[] chars)
    {
        additionalDangerousChars = chars.ToHashSet();
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum total output size in characters (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum size, or null for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder WithMaxOutputSize(long? maxSize)
    {
        maxOutputSize = maxSize;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum size for a single field in characters (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum size, or null for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder WithMaxFieldSize(int? maxSize)
    {
        maxFieldSize = maxSize;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of columns per row (DoS protection).
    /// </summary>
    /// <param name="maxColumns">The maximum columns, or null for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvWriterBuilder WithMaxColumnCount(int? maxColumns)
    {
        maxColumnCount = maxColumns;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Creates a writer for manual row-by-row writing.
    /// </summary>
    /// <param name="textWriter">The TextWriter to write to.</param>
    /// <param name="leaveOpen">When true, leaves the writer open.</param>
    /// <returns>A configured CsvStreamWriter.</returns>
    public CsvStreamWriter CreateWriter(TextWriter textWriter, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(textWriter);
        return new CsvStreamWriter(textWriter, GetOptions(), leaveOpen);
    }

    /// <summary>
    /// Creates a writer for a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>A configured CsvStreamWriter.</returns>
    public CsvStreamWriter CreateFileWriter(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var streamWriter = new StreamWriter(stream, encoding);
        return new CsvStreamWriter(streamWriter, GetOptions(), leaveOpen: false);
    }

    /// <summary>
    /// Creates a writer for a stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="leaveOpen">When true, leaves the stream open.</param>
    /// <returns>A configured CsvStreamWriter.</returns>
    public CsvStreamWriter CreateStreamWriter(Stream stream, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        // StreamWriter owns the stream, CsvStreamWriter owns the StreamWriter
        // When leaveOpen=false, StreamWriter disposes stream; CsvStreamWriter disposes StreamWriter
        var streamWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        return new CsvStreamWriter(streamWriter, GetOptions(), leaveOpen: false);
    }

    private CsvWriteOptions GetOptions()
    {
        return cachedOptions ??= new CsvWriteOptions
        {
            Delimiter = delimiter,
            Quote = quote,
            NewLine = newLine,
            QuoteStyle = quoteStyle,
            WriteHeader = false, // Manual writing doesn't auto-write headers
            Culture = culture,
            NullValue = nullValue,
            DateTimeFormat = dateTimeFormat,
            DateOnlyFormat = dateOnlyFormat,
            TimeOnlyFormat = timeOnlyFormat,
            NumberFormat = numberFormat,
            InjectionProtection = injectionProtection,
            AdditionalDangerousChars = additionalDangerousChars,
            MaxOutputSize = maxOutputSize,
            MaxFieldSize = maxFieldSize,
            MaxColumnCount = maxColumnCount
        };
    }
}

