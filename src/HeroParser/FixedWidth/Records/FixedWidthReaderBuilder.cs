using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.FixedWidths.Records.Binding;
using FixedWidthFactory = HeroParser.FixedWidth;

namespace HeroParser.FixedWidths.Records;

/// <summary>
/// Fluent builder for configuring and executing fixed-width file reading operations.
/// </summary>
/// <typeparam name="T">The record type to deserialize.</typeparam>
public sealed class FixedWidthReaderBuilder<T> where T : class, new()
{
    // Parser options
    private int? recordLength = null;
    private char defaultPadChar = ' ';
    private FieldAlignment defaultAlignment = FieldAlignment.Left;
    private int maxRecordCount = 100_000;
    private bool trackSourceLineNumbers = false;
    private bool skipEmptyLines = true;
    private char? commentCharacter = null;
    private int skipRows = 0;
    private long? maxInputSize = 100 * 1024 * 1024; // 100 MB default

    // Record options
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private FixedWidthDeserializeErrorHandler? onDeserializeError = null;
    private IReadOnlyList<string>? nullValues = null;
    private IProgress<FixedWidthProgress>? progress = null;
    private int progressIntervalRows = 1000;

    // Encoding for file/stream operations
    private Encoding encoding = Encoding.UTF8;

    // Cached options - invalidated when any setting changes
    private FixedWidthParserOptions? cachedOptions;

    internal FixedWidthReaderBuilder() { }

    #region Parser Options

    /// <summary>
    /// Sets the fixed record length for non-line-based parsing.
    /// When specified, records are read as fixed-length blocks without regard to line endings.
    /// </summary>
    /// <param name="length">The record length in characters.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithRecordLength(int length)
    {
        recordLength = length;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets line-based parsing mode where each line is a record (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> LineBased()
    {
        recordLength = null;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the default padding character for all fields.
    /// </summary>
    /// <param name="padChar">The padding character (default: space).</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithDefaultPadChar(char padChar)
    {
        defaultPadChar = padChar;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the default field alignment for all fields.
    /// </summary>
    /// <param name="alignment">The alignment determining which side to trim.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithDefaultAlignment(FieldAlignment alignment)
    {
        defaultAlignment = alignment;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum number of records to parse.
    /// </summary>
    /// <param name="maxCount">The maximum record count.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithMaxRecords(int maxCount)
    {
        maxRecordCount = maxCount;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Enables source line number tracking for error reporting.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> TrackLineNumbers()
    {
        trackSourceLineNumbers = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets whether to skip empty lines (only applies to line-based parsing).
    /// </summary>
    /// <param name="skip">True to skip empty lines (default), false to include them.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> SkipEmptyLines(bool skip = true)
    {
        skipEmptyLines = skip;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Includes empty lines as records (only applies to line-based parsing).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> IncludeEmptyLines()
    {
        skipEmptyLines = false;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the comment character. Lines starting with this character are skipped.
    /// Only applies to line-based parsing.
    /// </summary>
    /// <param name="commentChar">The comment character (e.g., '#').</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithCommentCharacter(char commentChar)
    {
        commentCharacter = commentChar;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Skips the specified number of rows before parsing data.
    /// </summary>
    /// <param name="rowCount">The number of rows to skip.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> SkipRows(int rowCount)
    {
        skipRows = rowCount;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum input size in bytes for file and stream operations.
    /// </summary>
    /// <param name="maxBytes">The maximum size in bytes, or null to disable the limit.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithMaxInputSize(long? maxBytes)
    {
        maxInputSize = maxBytes;
        InvalidateCache();
        return this;
    }

    #endregion

    #region Record Options

    /// <summary>
    /// Sets the culture for parsing values.
    /// </summary>
    /// <param name="culture">The culture to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithCulture(CultureInfo culture)
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
    public FixedWidthReaderBuilder<T> WithCulture(string cultureName)
    {
        culture = CultureInfo.GetCultureInfo(cultureName);
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the error handler for deserialization errors.
    /// </summary>
    /// <param name="handler">The error handler delegate.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> OnError(FixedWidthDeserializeErrorHandler handler)
    {
        onDeserializeError = handler;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets values that should be treated as null during parsing.
    /// </summary>
    /// <param name="values">The string values to treat as null.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithNullValues(params string[] values)
    {
        nullValues = values;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the progress reporter for receiving parsing progress updates.
    /// </summary>
    /// <param name="progressReporter">The progress reporter.</param>
    /// <param name="intervalRows">Rows between progress updates (default 1000).</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithProgress(IProgress<FixedWidthProgress> progressReporter, int intervalRows = 1000)
    {
        progress = progressReporter;
        progressIntervalRows = intervalRows;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the encoding for file and stream operations.
    /// </summary>
    /// <param name="encoding">The encoding to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder<T> WithEncoding(Encoding encoding)
    {
        this.encoding = encoding ?? Encoding.UTF8;
        return this;
    }

    #endregion

    #region Terminal Methods

    /// <summary>
    /// Reads records from a fixed-width string.
    /// </summary>
    /// <param name="text">The fixed-width content to parse.</param>
    /// <returns>An enumerable of deserialized records.</returns>
    public IEnumerable<T> FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var options = GetOptions();
        return FixedWidthRecordBinder<T>.Bind(
            FixedWidthFactory.ReadFromText(text, options),
            culture,
            onDeserializeError,
            nullValues,
            progress,
            progressIntervalRows);
    }

    /// <summary>
    /// Reads records from a fixed-width file.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <returns>An enumerable of deserialized records.</returns>
    public IEnumerable<T> FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var options = GetOptions();
        var fileInfo = new FileInfo(path);
        options.ValidateInputSize(fileInfo.Length);

        var text = File.ReadAllText(path, encoding);
        return FromText(text);
    }

    /// <summary>
    /// Reads records from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>An enumerable of deserialized records.</returns>
    public IEnumerable<T> FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var options = GetOptions();
        if (stream.CanSeek)
        {
            options.ValidateInputSize(stream.Length);
        }

        using var reader = new StreamReader(stream, encoding, leaveOpen: true);
        var text = reader.ReadToEnd();
        return FromText(text);
    }

    /// <summary>
    /// Reads records from a fixed-width file asynchronously.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of deserialized records.</returns>
    public async IAsyncEnumerable<T> FromFileAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var options = GetOptions();
        var fileInfo = new FileInfo(path);
        options.ValidateInputSize(fileInfo.Length);

        var text = await File.ReadAllTextAsync(path, encoding, cancellationToken).ConfigureAwait(false);
        foreach (var record in FromText(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }

    /// <summary>
    /// Reads records from a stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of deserialized records.</returns>
    public async IAsyncEnumerable<T> FromStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var options = GetOptions();
        if (stream.CanSeek)
        {
            options.ValidateInputSize(stream.Length);
        }

        using var reader = new StreamReader(stream, encoding, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        foreach (var record in FromText(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }

    /// <summary>
    /// Iterates over records from fixed-width text using a callback, reusing a single instance for minimal allocation.
    /// </summary>
    /// <remarks>
    /// <para><b>IMPORTANT:</b> This method uses object reuse to minimize allocations. The same record instance is passed to each callback invocation.
    /// Do not store the record instance directly - copy any needed values within the callback.</para>
    /// <para>String properties will still allocate new strings for each row.</para>
    /// </remarks>
    /// <param name="text">The fixed-width content to parse.</param>
    /// <param name="callback">The callback to invoke for each record.</param>
    public void ForEachFromText(string text, Action<T> callback)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(callback);

        var options = GetOptions();
        var reader = FixedWidthFactory.ReadFromText(text, options);
        FixedWidthRecordBinder<T>.ForEach(reader, culture, nullValues, callback);
    }

    /// <summary>
    /// Iterates over records from a file using a callback, reusing a single instance for minimal allocation.
    /// </summary>
    /// <remarks>
    /// <para><b>IMPORTANT:</b> This method uses object reuse to minimize allocations. The same record instance is passed to each callback invocation.
    /// Do not store the record instance directly - copy any needed values within the callback.</para>
    /// <para>String properties will still allocate new strings for each row.</para>
    /// </remarks>
    /// <param name="path">The file path to read from.</param>
    /// <param name="callback">The callback to invoke for each record.</param>
    public void ForEachFromFile(string path, Action<T> callback)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(callback);

        var options = GetOptions();
        var fileInfo = new FileInfo(path);
        options.ValidateInputSize(fileInfo.Length);

        var text = File.ReadAllText(path, encoding);
        ForEachFromText(text, callback);
    }

    #endregion

    #region Options Construction

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InvalidateCache()
    {
        cachedOptions = null;
    }

    private FixedWidthParserOptions GetOptions()
    {
        return cachedOptions ??= new FixedWidthParserOptions
        {
            RecordLength = recordLength,
            DefaultPadChar = defaultPadChar,
            DefaultAlignment = defaultAlignment,
            MaxRecordCount = maxRecordCount,
            TrackSourceLineNumbers = trackSourceLineNumbers,
            SkipEmptyLines = skipEmptyLines,
            CommentCharacter = commentCharacter,
            SkipRows = skipRows,
            MaxInputSize = maxInputSize
        };
    }


    #endregion
}

/// <summary>
/// Non-generic builder for manual row-by-row fixed-width file reading.
/// </summary>
public sealed class FixedWidthReaderBuilder
{
    // Parser options
    private int? recordLength = null;
    private char defaultPadChar = ' ';
    private FieldAlignment defaultAlignment = FieldAlignment.Left;
    private int maxRecordCount = 100_000;
    private bool trackSourceLineNumbers = false;
    private bool skipEmptyLines = true;
    private char? commentCharacter = null;
    private int skipRows = 0;
    private long? maxInputSize = 100 * 1024 * 1024; // 100 MB default

    // Encoding for file/stream operations
    private Encoding encoding = Encoding.UTF8;

    // Cached options - invalidated when any setting changes
    private FixedWidthParserOptions? cachedOptions;

    internal FixedWidthReaderBuilder() { }

    #region Parser Options

    /// <summary>
    /// Sets the fixed record length for non-line-based parsing.
    /// </summary>
    /// <param name="length">The record length in characters.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder WithRecordLength(int length)
    {
        recordLength = length;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets line-based parsing mode where each line is a record (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder LineBased()
    {
        recordLength = null;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the default padding character for all fields.
    /// </summary>
    /// <param name="padChar">The padding character (default: space).</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder WithDefaultPadChar(char padChar)
    {
        defaultPadChar = padChar;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the default field alignment for all fields.
    /// </summary>
    /// <param name="alignment">The alignment determining which side to trim.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder WithDefaultAlignment(FieldAlignment alignment)
    {
        defaultAlignment = alignment;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum number of records to parse.
    /// </summary>
    /// <param name="maxCount">The maximum record count.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder WithMaxRecords(int maxCount)
    {
        maxRecordCount = maxCount;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Enables source line number tracking for error reporting.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder TrackLineNumbers()
    {
        trackSourceLineNumbers = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets whether to skip empty lines (only applies to line-based parsing).
    /// </summary>
    /// <param name="skip">True to skip empty lines (default), false to include them.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder SkipEmptyLines(bool skip = true)
    {
        skipEmptyLines = skip;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Includes empty lines as records (only applies to line-based parsing).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder IncludeEmptyLines()
    {
        skipEmptyLines = false;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the comment character. Lines starting with this character are skipped.
    /// Only applies to line-based parsing.
    /// </summary>
    /// <param name="commentChar">The comment character (e.g., '#').</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder WithCommentCharacter(char commentChar)
    {
        commentCharacter = commentChar;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Skips the specified number of rows before parsing data.
    /// </summary>
    /// <param name="rowCount">The number of rows to skip.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder SkipRows(int rowCount)
    {
        skipRows = rowCount;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum input size in bytes for file and stream operations.
    /// </summary>
    /// <param name="maxBytes">The maximum size in bytes, or null to disable the limit.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder WithMaxInputSize(long? maxBytes)
    {
        maxInputSize = maxBytes;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the encoding for file and stream operations.
    /// </summary>
    /// <param name="encoding">The encoding to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthReaderBuilder WithEncoding(Encoding encoding)
    {
        this.encoding = encoding ?? Encoding.UTF8;
        return this;
    }

    #endregion

    #region Terminal Methods

    /// <summary>
    /// Creates a reader from fixed-width text for manual row-by-row reading.
    /// </summary>
    /// <param name="text">The fixed-width content to parse.</param>
    /// <returns>A configured FixedWidthCharSpanReader.</returns>
    public FixedWidthCharSpanReader FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return FixedWidthFactory.ReadFromText(text, GetOptions());
    }

    /// <summary>
    /// Reads text from a file and returns a reader for manual row-by-row reading.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <returns>The file content and a configured FixedWidthCharSpanReader.</returns>
    /// <remarks>
    /// Note: This method reads the entire file into memory. For large files,
    /// consider using streaming approaches.
    /// </remarks>
    public FixedWidthCharSpanReader FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var options = GetOptions();
        var fileInfo = new FileInfo(path);
        options.ValidateInputSize(fileInfo.Length);

        var text = File.ReadAllText(path, encoding);
        return FromText(text);
    }

    /// <summary>
    /// Reads text from a stream and returns a reader for manual row-by-row reading.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>A configured FixedWidthCharSpanReader.</returns>
    /// <remarks>
    /// Note: This method reads the entire stream into memory. For large streams,
    /// consider using streaming approaches.
    /// </remarks>
    public FixedWidthCharSpanReader FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var options = GetOptions();
        if (stream.CanSeek)
        {
            options.ValidateInputSize(stream.Length);
        }

        using var reader = new StreamReader(stream, encoding, leaveOpen: true);
        var text = reader.ReadToEnd();
        return FromText(text);
    }

    #endregion

    #region Private Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InvalidateCache()
    {
        cachedOptions = null;
    }

    private FixedWidthParserOptions GetOptions()
    {
        return cachedOptions ??= new FixedWidthParserOptions
        {
            RecordLength = recordLength,
            DefaultPadChar = defaultPadChar,
            DefaultAlignment = defaultAlignment,
            MaxRecordCount = maxRecordCount,
            TrackSourceLineNumbers = trackSourceLineNumbers,
            SkipEmptyLines = skipEmptyLines,
            CommentCharacter = commentCharacter,
            SkipRows = skipRows,
            MaxInputSize = maxInputSize
        };
    }

    #endregion
}

/// <summary>
/// Reports progress during fixed-width parsing.
/// </summary>
public readonly record struct FixedWidthProgress
{
    /// <summary>Gets the number of records processed so far.</summary>
    public int RecordsProcessed { get; init; }
}

/// <summary>
/// Delegate for handling deserialization errors in fixed-width parsing.
/// </summary>
/// <param name="context">Context about the error.</param>
/// <param name="exception">The exception that was thrown.</param>
/// <returns>The action to take (skip or throw).</returns>
public delegate FixedWidthDeserializeErrorAction FixedWidthDeserializeErrorHandler(
    FixedWidthErrorContext context,
    Exception exception);

/// <summary>
/// Action to take when a deserialization error occurs.
/// </summary>
public enum FixedWidthDeserializeErrorAction
{
    /// <summary>Skip the current record and continue.</summary>
    SkipRecord,
    /// <summary>Rethrow the exception.</summary>
    Throw
}

/// <summary>
/// Context information for deserialization errors.
/// </summary>
public readonly record struct FixedWidthErrorContext
{
    /// <summary>Gets the 1-based record number where the error occurred.</summary>
    public int RecordNumber { get; init; }

    /// <summary>Gets the 1-based source line number (if tracking is enabled).</summary>
    public int SourceLineNumber { get; init; }

    /// <summary>Gets the field name that failed to parse (if available).</summary>
    public string? FieldName { get; init; }

    /// <summary>Gets the raw value that failed to parse.</summary>
    public string? RawValue { get; init; }

    /// <summary>Gets the target property type.</summary>
    public Type? TargetType { get; init; }
}
