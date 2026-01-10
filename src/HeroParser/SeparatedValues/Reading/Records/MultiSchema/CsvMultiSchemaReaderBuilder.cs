using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.SeparatedValues.Reading.Records.MultiSchema;

/// <summary>
/// Represents a mapping from a discriminator value to a record type.
/// </summary>
internal readonly struct DiscriminatorMapping
{
    public readonly string StringValue;
    public readonly DiscriminatorKey? PackedKey;
    public readonly Type RecordType;
    public readonly Func<CsvRecordOptions?, object> CharBinderFactory;
    public readonly Func<CsvRecordOptions?, object> ByteBinderFactory;

    public DiscriminatorMapping(
        string stringValue,
        DiscriminatorKey? packedKey,
        Type recordType,
        Func<CsvRecordOptions?, object> charBinderFactory,
        Func<CsvRecordOptions?, object> byteBinderFactory)
    {
        StringValue = stringValue;
        PackedKey = packedKey;
        RecordType = recordType;
        CharBinderFactory = charBinderFactory;
        ByteBinderFactory = byteBinderFactory;
    }
}

/// <summary>
/// Fluent builder for configuring multi-schema CSV reading where different rows
/// map to different record types based on a discriminator column value.
/// </summary>
/// <remarks>
/// <para>
/// Multi-schema CSV parsing is common in banking and financial formats like NACHA, BAI, and EDI,
/// where files contain header, detail, and trailer records identified by a record type code.
/// </para>
/// <para>
/// This builder provides a zero-allocation fast path for discriminators up to 8 ASCII characters,
/// which covers the vast majority of real-world banking formats.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// foreach (var record in Csv.Read()
///     .WithMultiSchema()
///     .WithDiscriminator("RecordType")
///     .MapRecord&lt;HeaderRecord&gt;("H")
///     .MapRecord&lt;DetailRecord&gt;("D")
///     .MapRecord&lt;TrailerRecord&gt;("T")
///     .AllowMissingColumns()
///     .FromText(csv))
/// {
///     switch (record)
///     {
///         case HeaderRecord h: // ...
///         case DetailRecord d: // ...
///         case TrailerRecord t: // ...
///     }
/// }
/// </code>
/// </example>
public sealed class CsvMultiSchemaReaderBuilder
{
    // Parser options (inherited from CsvRowReaderBuilder)
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
    private bool trackSourceLineNumbers = false;

    // Record options
    private bool hasHeaderRow = true;
    private bool caseSensitiveHeaders = false;
    private bool allowMissingColumns = false;
    private IReadOnlyList<string>? nullValues = null;
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private int skipRows = 0;
    private IProgress<CsvProgress>? progress = null;
    private int progressIntervalRows = 1000;

    // Multi-schema config
    private int? discriminatorIndex;
    private string? discriminatorColumnName;
    private bool caseInsensitiveDiscriminator = false;
    private UnmatchedRowBehavior unmatchedBehavior = UnmatchedRowBehavior.Skip;
    private readonly List<DiscriminatorMapping> mappings = [];

    internal CsvMultiSchemaReaderBuilder() { }

    internal CsvMultiSchemaReaderBuilder(CsvParserOptions parserOptions)
    {
        delimiter = parserOptions.Delimiter;
        quote = parserOptions.Quote;
        maxColumnCount = parserOptions.MaxColumnCount;
        maxRowCount = parserOptions.MaxRowCount;
        useSimdIfAvailable = parserOptions.UseSimdIfAvailable;
        allowNewlinesInQuotes = parserOptions.AllowNewlinesInsideQuotes;
        enableQuotedFields = parserOptions.EnableQuotedFields;
        commentCharacter = parserOptions.CommentCharacter;
        trimFields = parserOptions.TrimFields;
        maxFieldSize = parserOptions.MaxFieldSize;
        escapeCharacter = parserOptions.EscapeCharacter;
        maxRowSize = parserOptions.MaxRowSize;
        trackSourceLineNumbers = parserOptions.TrackSourceLineNumbers;
    }

    #region Parser Options

    /// <summary>Sets the field delimiter character (default is comma).</summary>
    public CsvMultiSchemaReaderBuilder WithDelimiter(char newDelimiter)
    {
        delimiter = newDelimiter;
        return this;
    }

    /// <summary>Sets the quote character (default is double quote).</summary>
    public CsvMultiSchemaReaderBuilder WithQuote(char newQuote)
    {
        quote = newQuote;
        return this;
    }

    /// <summary>Sets the maximum number of columns per row.</summary>
    public CsvMultiSchemaReaderBuilder WithMaxColumnCount(int count)
    {
        maxColumnCount = count;
        return this;
    }

    /// <summary>Sets the maximum number of rows to parse.</summary>
    public CsvMultiSchemaReaderBuilder WithMaxRowCount(int count)
    {
        maxRowCount = count;
        return this;
    }

    /// <summary>Enables or disables SIMD acceleration.</summary>
    public CsvMultiSchemaReaderBuilder UseSimd(bool enabled = true)
    {
        useSimdIfAvailable = enabled;
        return this;
    }

    /// <summary>Enables support for newlines inside quoted fields.</summary>
    public CsvMultiSchemaReaderBuilder AllowNewlinesInQuotes(bool allow = true)
    {
        allowNewlinesInQuotes = allow;
        return this;
    }

    /// <summary>Enables or disables quoted field handling.</summary>
    public CsvMultiSchemaReaderBuilder EnableQuotedFields(bool enabled = true)
    {
        enableQuotedFields = enabled;
        return this;
    }

    /// <summary>Sets the comment character for skipping lines.</summary>
    public CsvMultiSchemaReaderBuilder WithCommentCharacter(char? commentChar)
    {
        commentCharacter = commentChar;
        return this;
    }

    /// <summary>Enables trimming of whitespace from field values.</summary>
    public CsvMultiSchemaReaderBuilder TrimFields(bool trim = true)
    {
        trimFields = trim;
        return this;
    }

    /// <summary>Sets the maximum field size in characters.</summary>
    public CsvMultiSchemaReaderBuilder WithMaxFieldSize(int? maxSize)
    {
        maxFieldSize = maxSize;
        return this;
    }

    /// <summary>Sets the escape character for escaping special characters.</summary>
    public CsvMultiSchemaReaderBuilder WithEscapeCharacter(char? escapeChar)
    {
        escapeCharacter = escapeChar;
        return this;
    }

    /// <summary>Sets the maximum row size for streaming readers (characters for UTF-16, bytes for UTF-8).</summary>
    public CsvMultiSchemaReaderBuilder WithMaxRowSize(int? maxSize)
    {
        maxRowSize = maxSize;
        return this;
    }

    /// <summary>Enables tracking of source line numbers.</summary>
    public CsvMultiSchemaReaderBuilder TrackSourceLineNumbers(bool track = true)
    {
        trackSourceLineNumbers = track;
        return this;
    }

    #endregion

    #region Record Options

    /// <summary>Specifies whether the CSV has a header row (default is true).</summary>
    public CsvMultiSchemaReaderBuilder HasHeaderRow(bool hasHeader = true)
    {
        hasHeaderRow = hasHeader;
        return this;
    }

    /// <summary>Specifies that the CSV has no header row.</summary>
    public CsvMultiSchemaReaderBuilder NoHeaderRow()
    {
        hasHeaderRow = false;
        return this;
    }

    /// <summary>Makes header name matching case-sensitive.</summary>
    public CsvMultiSchemaReaderBuilder CaseSensitiveHeaders(bool caseSensitive = true)
    {
        caseSensitiveHeaders = caseSensitive;
        return this;
    }

    /// <summary>Allows missing columns without throwing an exception.</summary>
    public CsvMultiSchemaReaderBuilder AllowMissingColumns(bool allow = true)
    {
        allowMissingColumns = allow;
        return this;
    }

    /// <summary>Specifies values to treat as null during parsing.</summary>
    public CsvMultiSchemaReaderBuilder WithNullValues(params string[] values)
    {
        nullValues = values;
        return this;
    }

    /// <summary>Sets the culture for parsing culture-sensitive values.</summary>
    public CsvMultiSchemaReaderBuilder WithCulture(CultureInfo newCulture)
    {
        culture = newCulture;
        return this;
    }

    /// <summary>Skips the specified number of rows from the start.</summary>
    public CsvMultiSchemaReaderBuilder SkipRows(int count)
    {
        skipRows = count;
        return this;
    }

    /// <summary>Sets the progress reporter for tracking parsing progress.</summary>
    public CsvMultiSchemaReaderBuilder WithProgress(IProgress<CsvProgress> progressReporter, int intervalRows = 1000)
    {
        progress = progressReporter;
        progressIntervalRows = intervalRows;
        return this;
    }

    #endregion

    #region Multi-Schema Configuration

    /// <summary>
    /// Specifies the discriminator column by zero-based index.
    /// </summary>
    /// <param name="columnIndex">The zero-based column index containing the record type code.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvMultiSchemaReaderBuilder WithDiscriminator(int columnIndex)
    {
        if (columnIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(columnIndex), "Column index must be non-negative.");

        discriminatorIndex = columnIndex;
        discriminatorColumnName = null;
        return this;
    }

    /// <summary>
    /// Specifies the discriminator column by header name.
    /// </summary>
    /// <param name="columnName">The header name of the column containing the record type code.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvMultiSchemaReaderBuilder WithDiscriminator(string columnName)
    {
        ArgumentNullException.ThrowIfNull(columnName);

        discriminatorColumnName = columnName;
        discriminatorIndex = null;
        return this;
    }

    /// <summary>
    /// Makes discriminator value matching case-insensitive.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvMultiSchemaReaderBuilder CaseInsensitiveDiscriminator(bool caseInsensitive = true)
    {
        caseInsensitiveDiscriminator = caseInsensitive;
        return this;
    }

    /// <summary>
    /// Maps a string discriminator value to a record type.
    /// </summary>
    /// <typeparam name="T">The record type to create for rows with this discriminator value.</typeparam>
    /// <param name="discriminatorValue">The discriminator value that identifies this record type.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvMultiSchemaReaderBuilder MapRecord<T>(string discriminatorValue) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(discriminatorValue);

        // Try to create packed key for fast lookup
        var normalizedValue = caseInsensitiveDiscriminator
            ? discriminatorValue.ToLowerInvariant()
            : discriminatorValue;

        DiscriminatorKey? packedKey = null;
        if (DiscriminatorKey.TryCreate(normalizedValue.AsSpan(), out var key))
        {
            packedKey = key;
        }

        mappings.Add(new DiscriminatorMapping(
            normalizedValue,
            packedKey,
            typeof(T),
            CsvRecordBinderFactory.GetCharBinder<T>,
            CsvRecordBinderFactory.GetByteBinder<T>));

        return this;
    }

    /// <summary>
    /// Maps an integer discriminator value to a record type.
    /// </summary>
    /// <typeparam name="T">The record type to create for rows with this discriminator value.</typeparam>
    /// <param name="discriminatorValue">The integer discriminator value that identifies this record type.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// This is a convenience overload for numeric discriminators common in formats like NACHA.
    /// The integer is converted to its string representation for matching.
    /// </remarks>
    public CsvMultiSchemaReaderBuilder MapRecord<T>(int discriminatorValue) where T : class, new()
    {
        return MapRecord<T>(discriminatorValue.ToString());
    }

    /// <summary>
    /// Specifies the behavior for rows that don't match any registered discriminator.
    /// </summary>
    /// <param name="behavior">The behavior to use for unmatched rows.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvMultiSchemaReaderBuilder OnUnmatchedRow(UnmatchedRowBehavior behavior)
    {
        unmatchedBehavior = behavior;
        return this;
    }

    #endregion

    #region Terminal Methods - Span-based

    /// <summary>
    /// Reads records from a CSV string.
    /// </summary>
    /// <param name="csvText">The CSV text to parse.</param>
    /// <returns>A reader that yields records of the registered types.</returns>
    public CsvMultiSchemaRecordReader<char> FromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        ValidateConfiguration();

        var parserOptions = GetParserOptions();
        parserOptions.Validate();

        var recordOptions = GetRecordOptions();
        var rowReader = new CsvRowReader<char>(csvText.AsSpan(), parserOptions);
        var binder = CreateCharBinder(recordOptions);

        return new CsvMultiSchemaRecordReader<char>(rowReader, binder, skipRows, progress, progressIntervalRows);
    }

    /// <summary>
    /// Reads records from UTF-8 encoded CSV data.
    /// </summary>
    /// <param name="data">The UTF-8 bytes to parse.</param>
    /// <returns>A reader that yields records of the registered types.</returns>
    public CsvMultiSchemaRecordReader<byte> FromBytes(ReadOnlySpan<byte> data)
    {
        ValidateConfiguration();

        var parserOptions = GetParserOptions();
        parserOptions.Validate();

        var recordOptions = GetRecordOptions();

        // Handle BOM
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            data = data[3..];

        var rowReader = new CsvRowReader<byte>(data, parserOptions);
        var binder = CreateByteBinder(recordOptions);

        return new CsvMultiSchemaRecordReader<byte>(rowReader, binder, skipRows, progress, progressIntervalRows);
    }

    #endregion

    #region Terminal Methods - Streaming

    /// <summary>
    /// Reads records from a CSV file.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF-8).</param>
    /// <returns>A streaming reader that yields records of the registered types.</returns>
    public CsvMultiSchemaStreamingRecordReader FromFile(string filePath, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        return FromStream(stream, encoding, leaveOpen: false);
    }

    /// <summary>
    /// Reads records from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF-8).</param>
    /// <param name="leaveOpen">Whether to leave the stream open after the reader is disposed.</param>
    /// <returns>A streaming reader that yields records of the registered types.</returns>
    public CsvMultiSchemaStreamingRecordReader FromStream(Stream stream, Encoding? encoding = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateConfiguration();

        var parserOptions = GetParserOptions();
        parserOptions.Validate();

        var recordOptions = GetRecordOptions();
        var binder = CreateCharBinder(recordOptions);

        return new CsvMultiSchemaStreamingRecordReader(
            stream,
            parserOptions,
            binder,
            encoding ?? Encoding.UTF8,
            leaveOpen,
            skipRows,
            progress,
            progressIntervalRows);
    }

    /// <summary>
    /// Reads records asynchronously from a CSV file.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF-8).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable that yields records of the registered types.</returns>
    public async IAsyncEnumerable<object> FromFileAsync(
        string filePath,
        Encoding? encoding = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        await using var reader = FromFile(filePath, encoding);
        while (await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return reader.Current;
        }
    }

    /// <summary>
    /// Reads records asynchronously from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF-8).</param>
    /// <param name="leaveOpen">Whether to leave the stream open after enumeration completes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable that yields records of the registered types.</returns>
    public async IAsyncEnumerable<object> FromStreamAsync(
        Stream stream,
        Encoding? encoding = null,
        bool leaveOpen = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        await using var reader = FromStream(stream, encoding, leaveOpen);
        while (await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return reader.Current;
        }
    }

    #endregion

    #region Private Helpers

    private void ValidateConfiguration()
    {
        if (discriminatorIndex is null && discriminatorColumnName is null)
        {
            throw new InvalidOperationException(
                "Discriminator column must be specified using WithDiscriminator().");
        }

        if (mappings.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one record type must be mapped using MapRecord<T>().");
        }

        if (discriminatorColumnName is not null && !hasHeaderRow)
        {
            throw new InvalidOperationException(
                "Cannot use discriminator column name without a header row. " +
                "Either use WithDiscriminator(columnIndex) or enable HasHeaderRow().");
        }
    }

    private CsvParserOptions GetParserOptions() => new()
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
        MaxRowSize = maxRowSize,
        TrackSourceLineNumbers = trackSourceLineNumbers
    };

    private CsvRecordOptions GetRecordOptions() => new()
    {
        HasHeaderRow = hasHeaderRow,
        CaseSensitiveHeaders = caseSensitiveHeaders,
        AllowMissingColumns = allowMissingColumns,
        NullValues = nullValues,
        Culture = culture,
        SkipRows = 0, // Handled separately
        Progress = null, // Handled separately
        ProgressIntervalRows = 1000
    };

    private CsvMultiSchemaBinder<char> CreateCharBinder(CsvRecordOptions recordOptions)
    {
        var packedLookup = new Dictionary<DiscriminatorKey, CsvMultiSchemaBinder<char>.BinderEntry>();
        Dictionary<string, CsvMultiSchemaBinder<char>.BinderEntry>? stringLookup = null;

        foreach (var mapping in mappings)
        {
            var binder = mapping.CharBinderFactory(recordOptions);
            var entry = CreateCharBinderEntry(binder, mapping.RecordType);

            if (mapping.PackedKey.HasValue)
            {
                packedLookup[mapping.PackedKey.Value] = entry;
            }
            else
            {
                stringLookup ??= new Dictionary<string, CsvMultiSchemaBinder<char>.BinderEntry>(
                    caseInsensitiveDiscriminator ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                stringLookup[mapping.StringValue] = entry;
            }
        }

        return new CsvMultiSchemaBinder<char>(
            discriminatorIndex,
            discriminatorColumnName,
            caseInsensitiveDiscriminator,
            unmatchedBehavior,
            packedLookup,
            stringLookup);
    }

    private CsvMultiSchemaBinder<byte> CreateByteBinder(CsvRecordOptions recordOptions)
    {
        var packedLookup = new Dictionary<DiscriminatorKey, CsvMultiSchemaBinder<byte>.BinderEntry>();
        Dictionary<string, CsvMultiSchemaBinder<byte>.BinderEntry>? stringLookup = null;

        foreach (var mapping in mappings)
        {
            var binder = mapping.ByteBinderFactory(recordOptions);
            var entry = CreateByteBinderEntry(binder, mapping.RecordType);

            if (mapping.PackedKey.HasValue)
            {
                packedLookup[mapping.PackedKey.Value] = entry;
            }
            else
            {
                stringLookup ??= new Dictionary<string, CsvMultiSchemaBinder<byte>.BinderEntry>(
                    caseInsensitiveDiscriminator ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                stringLookup[mapping.StringValue] = entry;
            }
        }

        return new CsvMultiSchemaBinder<byte>(
            discriminatorIndex,
            discriminatorColumnName,
            caseInsensitiveDiscriminator,
            unmatchedBehavior,
            packedLookup,
            stringLookup);
    }

    private static CsvMultiSchemaBinder<char>.BinderEntry CreateCharBinderEntry(object binder, Type recordType)
    {
        // Use reflection to create the properly typed entry
        var method = typeof(CsvMultiSchemaBinder<char>).GetMethod(
            "CreateEntry",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        var genericMethod = method!.MakeGenericMethod(recordType);
        return (CsvMultiSchemaBinder<char>.BinderEntry)genericMethod.Invoke(null, [binder])!;
    }

    private static CsvMultiSchemaBinder<byte>.BinderEntry CreateByteBinderEntry(object binder, Type recordType)
    {
        var method = typeof(CsvMultiSchemaBinder<byte>).GetMethod(
            "CreateEntry",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        var genericMethod = method!.MakeGenericMethod(recordType);
        return (CsvMultiSchemaBinder<byte>.BinderEntry)genericMethod.Invoke(null, [binder])!;
    }

    #endregion
}
