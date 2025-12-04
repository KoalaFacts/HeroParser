using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Span;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser.SeparatedValues.Reading.Records.MultiSchema;

/// <summary>
/// Fluent builder for configuring and executing multi-schema CSV reading operations.
/// </summary>
/// <remarks>
/// <para>
/// Multi-schema reading allows a single CSV file to contain different record types,
/// distinguished by a discriminator column value.
/// </para>
/// <para>
/// <strong>AOT/Trimming Compatibility:</strong> Multi-schema binding uses reflection internally
/// to create binders for the registered record types. For full Native AOT or trimming support,
/// ensure all mapped record types are decorated with the <c>[CsvGenerateBinder]</c> attribute
/// to use source-generated binders. Without source-generated binders, reflection-based binding
/// may fail in trimmed or AOT-compiled applications.
/// </para>
/// </remarks>
public sealed class CsvMultiSchemaReaderBuilder
{
    // Parser options (inherited from CsvReaderBuilder)
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
    private int skipRows = 0;

    // Multi-schema specific options
    private int? discriminatorColumnIndex = null;
    private string? discriminatorColumnName = null;
    private readonly Dictionary<string, Type> typeMappings = [];
    private CsvFallbackRowFactory? fallbackFactory = null;
    private UnmatchedRowBehavior unmatchedRowBehavior = UnmatchedRowBehavior.Throw;
    private StringComparison discriminatorComparison = StringComparison.Ordinal;

    // Encoding for file/stream operations
    private Encoding encoding = Encoding.UTF8;

    // Cached options - invalidated when any setting changes
    private CsvParserOptions? cachedParserOptions;

    internal CsvMultiSchemaReaderBuilder() { }

    /// <summary>
    /// Creates a new multi-schema reader builder by copying parser options from a non-generic reader builder.
    /// </summary>
    internal CsvMultiSchemaReaderBuilder(
        char delimiter,
        char quote,
        int maxColumnCount,
        int maxRowCount,
        bool useSimdIfAvailable,
        bool allowNewlinesInQuotes,
        bool enableQuotedFields,
        char? commentCharacter,
        bool trimFields,
        int? maxFieldSize,
        char? escapeCharacter,
        int? maxRowSize,
        Encoding encoding)
    {
        this.delimiter = delimiter;
        this.quote = quote;
        this.maxColumnCount = maxColumnCount;
        this.maxRowCount = maxRowCount;
        this.useSimdIfAvailable = useSimdIfAvailable;
        this.allowNewlinesInQuotes = allowNewlinesInQuotes;
        this.enableQuotedFields = enableQuotedFields;
        this.commentCharacter = commentCharacter;
        this.trimFields = trimFields;
        this.maxFieldSize = maxFieldSize;
        this.escapeCharacter = escapeCharacter;
        this.maxRowSize = maxRowSize;
        this.encoding = encoding;
    }

    #region Parser Options

    /// <summary>
    /// Sets the field delimiter character.
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithDelimiter(char delimiter)
    {
        this.delimiter = delimiter;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the quote character used for escaping.
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithQuote(char quote)
    {
        this.quote = quote;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum number of columns allowed per row.
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithMaxColumns(int maxColumnCount)
    {
        this.maxColumnCount = maxColumnCount;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum number of rows to parse.
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithMaxRows(int maxRowCount)
    {
        this.maxRowCount = maxRowCount;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Disables SIMD acceleration for parsing.
    /// </summary>
    public CsvMultiSchemaReaderBuilder DisableSimd()
    {
        useSimdIfAvailable = false;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Enables newline characters inside quoted fields (RFC 4180).
    /// </summary>
    public CsvMultiSchemaReaderBuilder AllowNewlinesInQuotes()
    {
        allowNewlinesInQuotes = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Disables quote handling for maximum speed.
    /// </summary>
    public CsvMultiSchemaReaderBuilder DisableQuotedFields()
    {
        enableQuotedFields = false;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the comment character to skip comment lines.
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithCommentCharacter(char commentChar)
    {
        commentCharacter = commentChar;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Enables trimming of whitespace from unquoted fields.
    /// </summary>
    public CsvMultiSchemaReaderBuilder TrimFields()
    {
        trimFields = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum allowed size for a single field (DoS protection).
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithMaxFieldSize(int maxSize)
    {
        maxFieldSize = maxSize;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the escape character for escaping special characters.
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithEscapeCharacter(char escapeChar)
    {
        escapeCharacter = escapeChar;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the maximum row size for streaming readers (DoS protection).
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithMaxRowSize(int? maxSize)
    {
        maxRowSize = maxSize;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets the encoding for file and stream operations.
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithEncoding(Encoding encoding)
    {
        this.encoding = encoding ?? Encoding.UTF8;
        return this;
    }

    #endregion

    #region Record Options

    /// <summary>
    /// Indicates that the CSV includes a header row (default).
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithHeader()
    {
        hasHeaderRow = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Indicates that the CSV does not include a header row.
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithoutHeader()
    {
        hasHeaderRow = false;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Enables case-sensitive header matching.
    /// </summary>
    public CsvMultiSchemaReaderBuilder CaseSensitiveHeaders()
    {
        caseSensitiveHeaders = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Allows missing columns without throwing an exception.
    /// </summary>
    public CsvMultiSchemaReaderBuilder AllowMissingColumns()
    {
        allowMissingColumns = true;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Sets values that should be treated as null during parsing.
    /// </summary>
    public CsvMultiSchemaReaderBuilder WithNullValues(params string[] values)
    {
        nullValues = values;
        InvalidateCache();
        return this;
    }

    /// <summary>
    /// Skips the specified number of rows before parsing data.
    /// </summary>
    public CsvMultiSchemaReaderBuilder SkipRows(int rowCount)
    {
        skipRows = rowCount;
        InvalidateCache();
        return this;
    }

    #endregion

    #region Multi-Schema Options

    /// <summary>
    /// Sets the discriminator column by zero-based index.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvMultiSchemaReaderBuilder WithDiscriminator(int columnIndex)
    {
        if (columnIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(columnIndex), "Column index must be non-negative.");

        discriminatorColumnIndex = columnIndex;
        discriminatorColumnName = null;
        return this;
    }

    /// <summary>
    /// Sets the discriminator column by header name.
    /// </summary>
    /// <param name="columnName">The column header name.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// Requires <see cref="WithHeader"/> to be enabled (default).
    /// </remarks>
    public CsvMultiSchemaReaderBuilder WithDiscriminator(string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        discriminatorColumnName = columnName;
        discriminatorColumnIndex = null;
        return this;
    }

    /// <summary>
    /// Maps a discriminator value to a record type.
    /// </summary>
    /// <typeparam name="T">The record type to create for matching rows.</typeparam>
    /// <param name="discriminatorValue">The discriminator value that identifies this record type.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// For AOT/trimming compatibility, decorate <typeparamref name="T"/> with <c>[CsvGenerateBinder]</c>
    /// to enable source-generated binding instead of reflection-based binding.
    /// </remarks>
    public CsvMultiSchemaReaderBuilder MapRecord<T>(string discriminatorValue) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(discriminatorValue);

        typeMappings[discriminatorValue] = typeof(T);
        return this;
    }

    /// <summary>
    /// Sets a custom factory for creating records from unmatched rows.
    /// </summary>
    /// <param name="factory">The factory function that receives the discriminator value, column values, and row number.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// The factory is invoked for rows that don't match any registered discriminator value.
    /// This also sets <see cref="OnUnmatchedRow"/> to <see cref="UnmatchedRowBehavior.UseFallback"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// .MapRecord((discriminator, columns, rowNum) => new UnknownRecord
    /// {
    ///     Type = discriminator,
    ///     RawData = columns
    /// })
    /// </code>
    /// </example>
    public CsvMultiSchemaReaderBuilder MapRecord(CsvFallbackRowFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        fallbackFactory = factory;
        unmatchedRowBehavior = UnmatchedRowBehavior.UseFallback;
        return this;
    }

    /// <summary>
    /// Sets the behavior for rows that don't match any registered discriminator value.
    /// </summary>
    /// <param name="behavior">The behavior to apply.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvMultiSchemaReaderBuilder OnUnmatchedRow(UnmatchedRowBehavior behavior)
    {
        unmatchedRowBehavior = behavior;
        return this;
    }

    /// <summary>
    /// Enables case-sensitive discriminator value matching.
    /// </summary>
    /// <param name="caseSensitive">
    /// <see langword="true"/> for case-sensitive matching (default);
    /// <see langword="false"/> for case-insensitive matching.
    /// </param>
    /// <returns>This builder for method chaining.</returns>
    public CsvMultiSchemaReaderBuilder CaseSensitiveDiscriminator(bool caseSensitive = true)
    {
        discriminatorComparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return this;
    }

    #endregion

    #region Terminal Methods

    /// <summary>
    /// Reads records from a CSV string.
    /// </summary>
    /// <param name="csvText">The CSV content to parse.</param>
    /// <returns>A reader for the deserialized records.</returns>
    public CsvMultiSchemaRecordReader FromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        ValidateConfiguration();

        var parserOptions = GetParserOptions();
        var recordOptions = GetRecordOptions();
        var reader = Csv.ReadFromCharSpan(csvText.AsSpan(), parserOptions);
        var binder = CreateBinder(recordOptions);

        return new CsvMultiSchemaRecordReader(reader, binder, skipRows);
    }

    /// <summary>
    /// Reads records from a CSV file.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <param name="bufferSize">Size of the internal buffer for reading; defaults to 16 KB.</param>
    /// <returns>A reader for the deserialized records. Dispose when done.</returns>
    public CsvMultiSchemaStreamingRecordReader FromFile(string path, int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ValidateConfiguration();

        var parserOptions = GetParserOptions();
        var recordOptions = GetRecordOptions();
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reader = Csv.ReadFromStream(stream, parserOptions, encoding, leaveOpen: false, bufferSize);
        var binder = CreateBinder(recordOptions);

        return new CsvMultiSchemaStreamingRecordReader(reader, binder, skipRows);
    }

    /// <summary>
    /// Reads records from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <param name="bufferSize">Size of the internal buffer for reading; defaults to 16 KB.</param>
    /// <returns>A reader for the deserialized records. Dispose when done.</returns>
    public CsvMultiSchemaStreamingRecordReader FromStream(Stream stream, bool leaveOpen = true, int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateConfiguration();

        var parserOptions = GetParserOptions();
        var recordOptions = GetRecordOptions();
        var reader = Csv.ReadFromStream(stream, parserOptions, encoding, leaveOpen, bufferSize);
        var binder = CreateBinder(recordOptions);

        return new CsvMultiSchemaStreamingRecordReader(reader, binder, skipRows);
    }

    /// <summary>
    /// Reads records from a CSV file asynchronously.
    /// </summary>
    /// <param name="path">The file path to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of deserialized records.</returns>
    public IAsyncEnumerable<object> FromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ValidateConfiguration();

        var parserOptions = GetParserOptions();
        var recordOptions = GetRecordOptions();
        var binder = CreateBinder(recordOptions);

        return ReadFromFileAsyncCore(path, parserOptions, binder, cancellationToken);
    }

    /// <summary>
    /// Reads records from a stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="leaveOpen">Whether to leave the stream open after reading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of deserialized records.</returns>
    public IAsyncEnumerable<object> FromStreamAsync(Stream stream, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateConfiguration();

        var parserOptions = GetParserOptions();
        var recordOptions = GetRecordOptions();
        var binder = CreateBinder(recordOptions);

        return ReadFromStreamAsyncCore(stream, parserOptions, binder, leaveOpen, cancellationToken);
    }

    private async IAsyncEnumerable<object> ReadFromFileAsyncCore(
        string path,
        CsvParserOptions parserOptions,
        CsvMultiSchemaBinder binder,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await foreach (var record in ReadFromStreamAsyncCore(stream, parserOptions, binder, leaveOpen: false, cancellationToken))
        {
            yield return record;
        }
    }

    private async IAsyncEnumerable<object> ReadFromStreamAsyncCore(
        Stream stream,
        CsvParserOptions parserOptions,
        CsvMultiSchemaBinder binder,
        bool leaveOpen,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var reader = Csv.CreateAsyncStreamReader(stream, parserOptions, encoding, leaveOpen);

        int rowNumber = 0;
        int skippedCount = 0;

        while (await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            rowNumber++;
            var row = reader.Current;

            // Skip rows if requested
            if (skippedCount < skipRows)
            {
                skippedCount++;
                continue;
            }

            // Process header if needed
            if (binder.NeedsHeaderResolution)
            {
                binder.BindHeader(row, rowNumber);
                continue;
            }

            var result = binder.Bind(row, rowNumber);
            if (result is not null)
            {
                yield return result;
            }
        }
    }

    #endregion

    #region Private Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InvalidateCache()
    {
        cachedParserOptions = null;
    }

    private void ValidateConfiguration()
    {
        if (discriminatorColumnIndex is null && discriminatorColumnName is null)
        {
            throw new InvalidOperationException(
                "Discriminator column must be configured using WithDiscriminator().");
        }

        if (discriminatorColumnName is not null && !hasHeaderRow)
        {
            throw new InvalidOperationException(
                "Discriminator by column name requires a header row. Use WithHeader() or WithDiscriminator(int columnIndex) instead.");
        }

        if (typeMappings.Count == 0 && fallbackFactory is null)
        {
            throw new InvalidOperationException(
                "At least one record type must be mapped using MapRecord<T>() or a fallback factory must be provided.");
        }
    }

    private CsvParserOptions GetParserOptions()
    {
        return cachedParserOptions ??= new CsvParserOptions
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

    private CsvRecordOptions GetRecordOptions()
    {
        return new CsvRecordOptions
        {
            HasHeaderRow = hasHeaderRow,
            CaseSensitiveHeaders = caseSensitiveHeaders,
            AllowMissingColumns = allowMissingColumns,
            NullValues = nullValues
        };
    }

    private CsvMultiSchemaBinder CreateBinder(CsvRecordOptions recordOptions)
    {
        return new CsvMultiSchemaBinder(
            discriminatorColumnIndex,
            discriminatorColumnName,
            typeMappings,
            fallbackFactory,
            unmatchedRowBehavior,
            discriminatorComparison,
            recordOptions,
            hasHeaderRow,
            caseSensitiveHeaders);
    }

    #endregion
}
