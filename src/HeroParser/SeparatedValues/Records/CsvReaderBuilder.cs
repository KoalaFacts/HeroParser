using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.SeparatedValues.Records.Readers;
using HeroParser.SeparatedValues.Streaming;

namespace HeroParser.SeparatedValues.Records;

/// <summary>
/// Fluent builder for configuring and executing CSV reading operations.
/// </summary>
/// <typeparam name="T">The record type to deserialize.</typeparam>
public sealed class CsvReaderBuilder<T> where T : class, new()
{
    // Parser options
    private char delimiter = ',';
    private char quote = '"';
    private int maxColumnCount = 100;
    private int maxRowCount = 100_000;
    private bool useSimdIfAvailable = true;
    private bool allowNewlinesInQuotes = false;
    private bool enableQuotedFields = true;
    private char? commentCharacter = null;
    private bool trimFields = false;
    private int? maxFieldSize = null;
    private char? escapeCharacter = null;
    private int? maxRowSize = 512 * 1024;

    // Record options
    private bool hasHeaderRow = true;
    private bool caseSensitiveHeaders = false;
    private bool allowMissingColumns = false;
    private IReadOnlyList<string>? nullValues = null;
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private int skipRows = 0;
    private bool detectDuplicateHeaders = false;
    private CsvDeserializeErrorHandler? onDeserializeError = null;
    private IReadOnlyList<string>? requiredHeaders = null;
    private CsvHeaderValidator? validateHeaders = null;
    private IProgress<CsvProgress>? progress = null;
    private int progressIntervalRows = 1000;
    private List<Func<CsvRecordOptions, CsvRecordOptions>>? converterRegistrations;

    // Encoding for file/stream operations
    private Encoding encoding = Encoding.UTF8;

    // Cached options - invalidated when any setting changes
    private CsvParserOptions? cachedParserOptions;
    private CsvRecordOptions? cachedRecordOptions;

    internal CsvReaderBuilder() { }

    #region Parser Options

    /// <summary>
    /// Sets the field delimiter character.
    /// </summary>
    /// <param name="delimiter">The delimiter character (must be ASCII).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithDelimiter(char delimiter)
    {
        this.delimiter = delimiter;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the quote character used for escaping.
    /// </summary>
    /// <param name="quote">The quote character (must be ASCII).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithQuote(char quote)
    {
        this.quote = quote;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum number of columns allowed per row.
    /// </summary>
    /// <param name="maxColumnCount">The maximum column count.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithMaxColumns(int maxColumnCount)
    {
        this.maxColumnCount = maxColumnCount;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum number of rows to parse.
    /// </summary>
    /// <param name="maxRowCount">The maximum row count.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithMaxRows(int maxRowCount)
    {
        this.maxRowCount = maxRowCount;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Disables SIMD acceleration for parsing.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> DisableSimd()
    {
        useSimdIfAvailable = false;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Enables newline characters inside quoted fields (RFC 4180).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> AllowNewlinesInQuotes()
    {
        allowNewlinesInQuotes = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Disables quote handling for maximum speed.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> DisableQuotedFields()
    {
        enableQuotedFields = false;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the comment character to skip comment lines.
    /// </summary>
    /// <param name="commentChar">The comment character (e.g., '#').</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithCommentCharacter(char commentChar)
    {
        commentCharacter = commentChar;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Enables trimming of whitespace from unquoted fields.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> TrimFields()
    {
        trimFields = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum allowed size for a single field (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum field size in characters.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithMaxFieldSize(int maxSize)
    {
        maxFieldSize = maxSize;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the escape character for escaping special characters.
    /// </summary>
    /// <param name="escapeChar">The escape character (e.g., '\\').</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithEscapeCharacter(char escapeChar)
    {
        escapeCharacter = escapeChar;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum row size for streaming readers (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum row size in characters, or null to disable.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithMaxRowSize(int? maxSize)
    {
        maxRowSize = maxSize;
        InvalidateCache();
        return this;
    }

    #endregion

    #region Record Options

    /// <summary>
    /// Indicates that the CSV includes a header row (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithHeader()
    {
        hasHeaderRow = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Indicates that the CSV does not include a header row.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithoutHeader()
    {
        hasHeaderRow = false;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Enables case-sensitive header matching.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> CaseSensitiveHeaders()
    {
        caseSensitiveHeaders = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Allows missing columns without throwing an exception.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> AllowMissingColumns()
    {
        allowMissingColumns = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets values that should be treated as null during parsing.
    /// </summary>
    /// <param name="values">The string values to treat as null.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithNullValues(params string[] values)
    {
        nullValues = values;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the culture for parsing values.
    /// </summary>
    /// <param name="culture">The culture to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithCulture(CultureInfo culture)
    {
        this.culture = culture ?? CultureInfo.InvariantCulture;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the culture for parsing values using a culture name.
    /// </summary>
    /// <param name="cultureName">The culture name (e.g., "en-US", "de-DE").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithCulture(string cultureName)
    {
        culture = CultureInfo.GetCultureInfo(cultureName);
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Skips the specified number of rows before parsing data.
    /// </summary>
    /// <param name="rowCount">The number of rows to skip.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> SkipRows(int rowCount)
    {
        skipRows = rowCount;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Enables detection of duplicate header names.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> DetectDuplicateHeaders()
    {
        detectDuplicateHeaders = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the error handler for deserialization errors.
    /// </summary>
    /// <param name="handler">The error handler delegate.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> OnError(CsvDeserializeErrorHandler handler)
    {
        onDeserializeError = handler;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the required headers that must be present in the CSV.
    /// </summary>
    /// <param name="headers">The required header names.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> RequireHeaders(params string[] headers)
    {
        requiredHeaders = headers;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets a custom header validation callback.
    /// </summary>
    /// <param name="validator">The header validator delegate.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> ValidateHeaders(CsvHeaderValidator validator)
    {
        validateHeaders = validator;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the progress reporter for receiving parsing progress updates.
    /// </summary>
    /// <param name="progress">The progress reporter.</param>
    /// <param name="intervalRows">Rows between progress updates (default 1000).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithProgress(IProgress<CsvProgress> progress, int intervalRows = 1000)
    {
        this.progress = progress;
        progressIntervalRows = intervalRows;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Registers a custom type converter.
    /// </summary>
    /// <typeparam name="TValue">The type to convert to.</typeparam>
    /// <param name="converter">The converter delegate.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> RegisterConverter<TValue>(CsvTypeConverter<TValue> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);

        converterRegistrations ??= [];
        // Store a func that captures the typed converter and returns the new options
        converterRegistrations.Add(options => options.RegisterConverter(converter));
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the encoding for file and stream operations.
    /// </summary>
    /// <param name="encoding">The encoding to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder<T> WithEncoding(Encoding encoding)
    {
        this.encoding = encoding ?? Encoding.UTF8;
        return this;
    }

    #endregion

    #region Terminal Methods

    /// <summary>
    /// Reads records from a CSV string.
    /// </summary>
    /// <param name="csvText">The CSV content to parse.</param>
    /// <returns>A reader for the deserialized records. Dispose when done.</returns>
    public CsvRecordReader<T> FromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);

        var (parserOptions, recordOptions) = GetOptions();
        return Csv.DeserializeRecords<T>(csvText, recordOptions, parserOptions);
    }

    /// <summary>
    /// Reads records from a CSV file.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <returns>A reader for the deserialized records. Dispose when done.</returns>
    public CsvStreamingRecordReader<T> FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var (parserOptions, recordOptions) = GetOptions();
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Csv.DeserializeRecords<T>(stream, recordOptions, parserOptions, encoding, leaveOpen: false);
    }

    /// <summary>
    /// Reads records from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <returns>A reader for the deserialized records. Dispose when done.</returns>
    public CsvStreamingRecordReader<T> FromStream(Stream stream, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var (parserOptions, recordOptions) = GetOptions();
        return Csv.DeserializeRecords<T>(stream, recordOptions, parserOptions, encoding, leaveOpen);
    }

    /// <summary>
    /// Reads records from a CSV file asynchronously.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of deserialized records.</returns>
    public IAsyncEnumerable<T> FromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var (parserOptions, recordOptions) = GetOptions();
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        return Csv.DeserializeRecordsAsync<T>(stream, recordOptions, parserOptions, encoding, leaveOpen: false, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads records from a stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of deserialized records.</returns>
    public IAsyncEnumerable<T> FromStreamAsync(Stream stream, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var (parserOptions, recordOptions) = GetOptions();
        return Csv.DeserializeRecordsAsync<T>(stream, recordOptions, parserOptions, encoding, leaveOpen, cancellationToken: cancellationToken);
    }

    #endregion

    #region Options Construction

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InvalidateCache()
    {
        cachedParserOptions = null;
        cachedRecordOptions = null;
    }

    private (CsvParserOptions parser, CsvRecordOptions record) GetOptions()
    {
        var parser = cachedParserOptions ??= new CsvParserOptions
        {
            Delimiter = delimiter,
            Quote = quote,
            MaxColumnCount = maxColumnCount,
            MaxRowCount = maxRowCount,
            UseSimdIfAvailable = useSimdIfAvailable,
            AllowNewlinesInsideQuotes = allowNewlinesInQuotes,
            EnableQuotedFields = enableQuotedFields,
            CommentCharacter = commentCharacter,
            TrimFields = trimFields,
            MaxFieldSize = maxFieldSize,
            EscapeCharacter = escapeCharacter,
            MaxRowSize = maxRowSize
        };

        var record = cachedRecordOptions ??= CreateRecordOptions();

        return (parser, record);
    }

    private CsvRecordOptions CreateRecordOptions()
    {
        var options = new CsvRecordOptions
        {
            HasHeaderRow = hasHeaderRow,
            CaseSensitiveHeaders = caseSensitiveHeaders,
            AllowMissingColumns = allowMissingColumns,
            NullValues = nullValues,
            Culture = culture,
            SkipRows = skipRows,
            DetectDuplicateHeaders = detectDuplicateHeaders,
            OnDeserializeError = onDeserializeError,
            RequiredHeaders = requiredHeaders,
            ValidateHeaders = validateHeaders,
            Progress = progress,
            ProgressIntervalRows = progressIntervalRows
        };

        // Apply custom converter registrations
        if (converterRegistrations is { Count: > 0 })
        {
            foreach (var registration in converterRegistrations)
            {
                options = registration(options);
            }
        }

        return options;
    }

    #endregion
}

/// <summary>
/// Non-generic builder for manual row-by-row CSV reading.
/// </summary>
public sealed class CsvReaderBuilder
{
    // Parser options
    private char delimiter = ',';
    private char quote = '"';
    private int maxColumnCount = 100;
    private int maxRowCount = 100_000;
    private bool useSimdIfAvailable = true;
    private bool allowNewlinesInQuotes = false;
    private bool enableQuotedFields = true;
    private char? commentCharacter = null;
    private bool trimFields = false;
    private int? maxFieldSize = null;
    private char? escapeCharacter = null;
    private int? maxRowSize = 512 * 1024;

    // Encoding for file/stream operations
    private Encoding encoding = Encoding.UTF8;

    // Cached options - invalidated when any setting changes
    private CsvParserOptions? cachedOptions;

    internal CsvReaderBuilder() { }

    #region Parser Options

    /// <summary>
    /// Sets the field delimiter character.
    /// </summary>
    /// <param name="delimiter">The delimiter character (must be ASCII).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder WithDelimiter(char delimiter)
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
    public CsvReaderBuilder WithQuote(char quote)
    {
        this.quote = quote;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of columns allowed per row.
    /// </summary>
    /// <param name="maxColumnCount">The maximum column count.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder WithMaxColumns(int maxColumnCount)
    {
        this.maxColumnCount = maxColumnCount;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of rows to parse.
    /// </summary>
    /// <param name="maxRowCount">The maximum row count.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder WithMaxRows(int maxRowCount)
    {
        this.maxRowCount = maxRowCount;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Disables SIMD acceleration for parsing.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder DisableSimd()
    {
        useSimdIfAvailable = false;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Enables newline characters inside quoted fields (RFC 4180).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder AllowNewlinesInQuotes()
    {
        allowNewlinesInQuotes = true;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Disables quote handling for maximum speed.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder DisableQuotedFields()
    {
        enableQuotedFields = false;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the comment character to skip comment lines.
    /// </summary>
    /// <param name="commentChar">The comment character (e.g., '#').</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder WithCommentCharacter(char commentChar)
    {
        commentCharacter = commentChar;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Enables trimming of whitespace from unquoted fields.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder TrimFields()
    {
        trimFields = true;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum allowed size for a single field (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum field size in characters.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder WithMaxFieldSize(int maxSize)
    {
        maxFieldSize = maxSize;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the escape character for escaping special characters.
    /// </summary>
    /// <param name="escapeChar">The escape character (e.g., '\\').</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder WithEscapeCharacter(char escapeChar)
    {
        escapeCharacter = escapeChar;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum row size for streaming readers (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum row size in characters, or null to disable.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder WithMaxRowSize(int? maxSize)
    {
        maxRowSize = maxSize;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the encoding for file and stream operations.
    /// </summary>
    /// <param name="encoding">The encoding to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvReaderBuilder WithEncoding(Encoding encoding)
    {
        this.encoding = encoding ?? Encoding.UTF8;
        // Note: encoding is not part of CsvParserOptions, so no need to invalidate cachedOptions
        return this;
    }

    #endregion

    #region Terminal Methods

    /// <summary>
    /// Creates a reader from CSV text for manual row-by-row reading.
    /// </summary>
    /// <param name="csvText">The CSV content to parse.</param>
    /// <returns>A configured CsvCharSpanReader.</returns>
    public CsvCharSpanReader FromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        return Csv.ReadFromText(csvText, GetOptions());
    }

    /// <summary>
    /// Creates a reader from a CSV file for manual row-by-row reading.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <param name="bufferSize">Size of the internal buffer for reading; defaults to 16 KB.</param>
    /// <returns>A configured CsvStreamReader. Dispose when done.</returns>
    public CsvStreamReader FromFile(string path, int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Csv.ReadFromFile(path, GetOptions(), encoding, bufferSize);
    }

    /// <summary>
    /// Creates a reader from a stream for manual row-by-row reading.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <param name="bufferSize">Size of the internal buffer for reading; defaults to 16 KB.</param>
    /// <returns>A configured CsvStreamReader. Dispose when done.</returns>
    public CsvStreamReader FromStream(Stream stream, bool leaveOpen = true, int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Csv.ReadFromStream(stream, GetOptions(), encoding, leaveOpen, bufferSize);
    }

    /// <summary>
    /// Creates an async streaming reader from a CSV file for manual row-by-row reading.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <param name="bufferSize">Size of the internal buffer for reading; defaults to 16 KB.</param>
    /// <returns>A configured CsvAsyncStreamReader. Dispose when done.</returns>
    public CsvAsyncStreamReader FromFileAsync(string path, int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Csv.CreateAsyncStreamReader(path, GetOptions(), encoding, bufferSize);
    }

    /// <summary>
    /// Creates an async streaming reader from a stream for manual row-by-row reading.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <param name="bufferSize">Size of the internal buffer for reading; defaults to 16 KB.</param>
    /// <returns>A configured CsvAsyncStreamReader. Dispose when done.</returns>
    public CsvAsyncStreamReader FromStreamAsync(Stream stream, bool leaveOpen = true, int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Csv.CreateAsyncStreamReader(stream, GetOptions(), encoding, leaveOpen, bufferSize);
    }

    #endregion

    #region Private Helpers

    private CsvParserOptions GetOptions()
    {
        return cachedOptions ??= new CsvParserOptions
        {
            Delimiter = delimiter,
            Quote = quote,
            MaxColumnCount = maxColumnCount,
            MaxRowCount = maxRowCount,
            UseSimdIfAvailable = useSimdIfAvailable,
            AllowNewlinesInsideQuotes = allowNewlinesInQuotes,
            EnableQuotedFields = enableQuotedFields,
            CommentCharacter = commentCharacter,
            TrimFields = trimFields,
            MaxFieldSize = maxFieldSize,
            EscapeCharacter = escapeCharacter,
            MaxRowSize = maxRowSize
        };
    }

    #endregion
}
