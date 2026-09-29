using System.Globalization;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser.SeparatedValues.Detection;

/// <summary>
/// Represents an inferred column type from CSV data analysis.
/// </summary>
public enum CsvInferredType
{
    /// <summary>Text/string data.</summary>
    String,
    /// <summary>32-bit integer values.</summary>
    Integer,
    /// <summary>64-bit integer values (exceeding int range).</summary>
    Long,
    /// <summary>Decimal/floating-point numeric values.</summary>
    Decimal,
    /// <summary>Boolean true/false values.</summary>
    Boolean,
    /// <summary>Date and/or time values.</summary>
    DateTime,
    /// <summary>GUID/UUID values.</summary>
    Guid
}

/// <summary>
/// Describes an inferred column in a CSV schema.
/// </summary>
/// <param name="Name">The column header name.</param>
/// <param name="InferredType">The detected data type.</param>
/// <param name="IsNullable">Whether any empty/null values were observed.</param>
/// <param name="MaxLength">The maximum string length observed for this column.</param>
public sealed record CsvInferredColumn(
    string Name,
    CsvInferredType InferredType,
    bool IsNullable,
    int MaxLength);

/// <summary>
/// The result of schema inference on CSV data.
/// </summary>
/// <param name="Columns">The inferred column definitions.</param>
/// <param name="SampledRowCount">The number of data rows sampled.</param>
public sealed record CsvSchemaInferenceResult(
    IReadOnlyList<CsvInferredColumn> Columns,
    int SampledRowCount);

/// <summary>
/// Options for controlling schema inference behavior.
/// </summary>
public sealed record CsvSchemaInferenceOptions
{
    /// <summary>
    /// Gets or sets the delimiter character (default: auto-detect or comma).
    /// </summary>
    public char? Delimiter { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of data rows to sample (default: 100).
    /// </summary>
    public int SampleRows { get; init; } = 100;

    /// <summary>Gets or sets the maximum number of columns for inference (default: 100).</summary>
    public int MaxColumnCount { get; init; } = 100;

    /// <summary>Gets or sets the maximum size of a sampled row (default: 1,048,576 units).</summary>
    /// <remarks>
    /// Measured in characters for string input and UTF-8 bytes for file or stream input.
    /// Increase this limit explicitly for trusted input with larger rows.
    /// </remarks>
    public int MaxRowSize { get; init; } = 1024 * 1024;

    /// <summary>Gets or sets the maximum input scanned to infer a schema (default: 128 MiB).</summary>
    /// <remarks>
    /// Measured in characters for string input and source bytes for file or stream input.
    /// The limit does not reject unexamined data after the requested sample has been collected.
    /// </remarks>
    public long MaxScannedInputSize { get; init; } = 128L * 1024 * 1024;

    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static CsvSchemaInferenceOptions Default { get; } = new();
}

/// <summary>
/// Analyzes CSV data to infer column types from sample rows.
/// </summary>
/// <remarks>
/// <para>
/// The inference algorithm samples data rows and attempts to parse each column value
/// against type candidates in order of specificity: Boolean, Integer, Long, Decimal,
/// Guid, DateTime, and finally String as a fallback.
/// </para>
/// <para>
/// Thread-Safety: All methods are thread-safe as they operate on local state only.
/// </para>
/// </remarks>
public static class CsvSchemaInference
{
    private const int DETECTION_SAMPLE_BYTES = 64 * 1024;

    /// <summary>Infers a UTF-8 or BOM-marked UTF-16 CSV file's schema from a bounded number of data rows.</summary>
    /// <param name="path">Path to the CSV file.</param>
    /// <param name="options">Inference and parsing options.</param>
    /// <param name="cancellationToken">Cancels file reads.</param>
    /// <returns>Column types inferred only from the sampled rows.</returns>
    public static async Task<CsvSchemaInferenceResult> InferFileAsync(
        string path, CsvSchemaInferenceOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= CsvSchemaInferenceOptions.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.SampleRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxColumnCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxRowSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRowSize, CsvAsyncStreamReader.ABSOLUTE_MAX_BUFFER_SIZE);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxScannedInputSize);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await InferAsync(stream, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Infers a stream's CSV schema from a bounded number of rows, leaving the stream open.</summary>
    /// <param name="stream">A readable UTF-8 stream or a BOM-marked UTF-16 stream.</param>
    /// <param name="options">Inference options. Non-seekable streams require an explicit delimiter.</param>
    /// <param name="cancellationToken">Cancels reads.</param>
    /// <returns>Column types inferred only from the sampled rows.</returns>
    public static async Task<CsvSchemaInferenceResult> InferAsync(
        Stream stream, CsvSchemaInferenceOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(stream));

        options ??= CsvSchemaInferenceOptions.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.SampleRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxColumnCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxRowSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRowSize, CsvAsyncStreamReader.ABSOLUTE_MAX_BUFFER_SIZE);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxScannedInputSize);
        if (!stream.CanSeek && options.Delimiter is null)
            throw new ArgumentException("Non-seekable streams require an explicit delimiter.", nameof(options));

        cancellationToken.ThrowIfCancellationRequested();
        long? initialPosition = stream.CanSeek ? stream.Position : null;
        try
        {
            var prefix = new byte[(int)Math.Min(options.MaxScannedInputSize,
                options.Delimiter is null ? DETECTION_SAMPLE_BYTES : 2)];
            int length = await ReadPrefixAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
            bool hasMore = stream.CanSeek && stream.Position < stream.Length;
            char delimiter;
            try
            {
                delimiter = options.Delimiter ?? DetectSampleDelimiter(prefix.AsSpan(0, length), hasMore);
            }
            catch (InvalidOperationException) when (hasMore && length == options.MaxScannedInputSize)
            {
                throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: true);
            }
            Encoding? encoding = GetUtf16Encoding(prefix.AsSpan(0, length));

            int bomLength = encoding is null ? 0 : 2;
            var boundedInput = new CsvPrefixReadStream(stream, prefix.AsMemory(bomLength, length - bomLength),
                options.MaxScannedInputSize, length);
            Stream input = boundedInput;
            if (encoding is not null)
                input = new Utf16ToUtf8ReadStream(input, encoding);

            await using var ownedInput = input;
            await using var reader = Csv.Read()
                .WithDelimiter(delimiter)
                .WithMaxColumns(options.MaxColumnCount)
                .WithMaxRows(int.MaxValue)
                .WithMaxRowSize(options.MaxRowSize)
                .AllowNewlinesInQuotes()
                .TrackSourceLineNumbers()
                .FromStreamAsync(ownedInput, leaveOpen: true);

            return await InferRowsAsync(reader, boundedInput, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (initialPosition.HasValue)
                stream.Position = initialPosition.Value;
        }
    }

    private static async Task<CsvSchemaInferenceResult> InferRowsAsync(
        CsvAsyncStreamReader reader, CsvPrefixReadStream boundedInput, CsvSchemaInferenceOptions options,
        CancellationToken cancellationToken)
    {
        bool hasHeader;
        try
        {
            hasHeader = await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CsvException ex) when (boundedInput.HasMoreThanBudget && !ex.IsRowSizeLimitExceeded)
        {
            throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: true);
        }
        if (!hasHeader)
        {
            if (boundedInput.HasMoreThanBudget)
                throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: true);
            throw new InvalidOperationException("Cannot infer schema from empty data.");
        }
        if (!reader.CurrentHadLineEnding &&
            await boundedInput.HasMoreAfterBudgetAsync(cancellationToken).ConfigureAwait(false))
            throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: true);

        var header = reader.Current;
        var headers = new string[header.ColumnCount];
        var candidates = new ColumnTypeTracker[header.ColumnCount];
        int embeddedLines = 0;
        for (int i = 0; i < headers.Length; i++)
        {
            headers[i] = header.GetString(i);
            embeddedLines += CountNewlines(headers[i].AsSpan());
            candidates[i] = new ColumnTypeTracker();
        }

        bool hasSkippedEmptyRows = header.SourceLineNumber > 1;
        int expectedNextLine = header.SourceLineNumber + embeddedLines + 1;
        int sampledRows = 0;
        while (sampledRows < options.SampleRows)
        {
            bool hasRow;
            try
            {
                hasRow = await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (CsvException ex) when (boundedInput.HasMoreThanBudget && !ex.IsRowSizeLimitExceeded)
            {
                throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: true);
            }
            catch (CsvException ex) when (ex.ErrorCode == CsvErrorCode.ParseError && !ex.IsRowSizeLimitExceeded)
            {
                break;
            }
            if (!hasRow)
            {
                if (boundedInput.HasMoreThanBudget)
                    throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: true);
                if (reader.NextSourceLineNumber > expectedNextLine)
                    hasSkippedEmptyRows = true;
                break;
            }

            if (!reader.CurrentHadLineEnding &&
                await boundedInput.HasMoreAfterBudgetAsync(cancellationToken).ConfigureAwait(false))
                throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: true);

            var row = reader.Current;
            if (row.SourceLineNumber > expectedNextLine)
                hasSkippedEmptyRows = true;

            embeddedLines = 0;
            for (int i = 0; i < Math.Min(headers.Length, row.ColumnCount); i++)
            {
                string value = row.GetString(i);
                candidates[i].Observe(value);
                embeddedLines += CountNewlines(value.AsSpan());
            }
            for (int i = headers.Length; i < row.ColumnCount; i++)
                embeddedLines += CountNewlines(row.GetString(i).AsSpan());
            for (int i = row.ColumnCount; i < headers.Length; i++)
                candidates[i].Observe("");

            expectedNextLine = row.SourceLineNumber + embeddedLines + 1;
            sampledRows++;
        }

        if (hasSkippedEmptyRows)
        {
            foreach (var candidate in candidates)
                candidate.HasNulls = true;
        }

        var columns = new CsvInferredColumn[headers.Length];
        for (int i = 0; i < columns.Length; i++)
            columns[i] = new CsvInferredColumn(headers[i], candidates[i].GetInferredType(),
                candidates[i].HasNulls, candidates[i].MaxLength);
        return new CsvSchemaInferenceResult(columns, sampledRows);
    }

    private static async Task<int> ReadPrefixAsync(Stream stream, byte[] sample, CancellationToken cancellationToken)
    {
        int length = 0;
        while (length < sample.Length)
        {
            int read = await stream.ReadAsync(sample.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            length += read;
        }
        return length;
    }

    private static char DetectSampleDelimiter(ReadOnlySpan<byte> sample, bool hasMore)
    {
        try
        {
            Encoding? encoding = GetUtf16Encoding(sample);
            if (encoding is not null)
            {
                ReadOnlySpan<byte> payload = sample[2..];
                return CsvDelimiterDetector.DetectDelimiter(encoding.GetString(payload[..(payload.Length & ~1)]));
            }
            return CsvDelimiterDetector.DetectDelimiter(sample);
        }
        catch (InvalidOperationException)
        {
            if (hasMore)
                throw new InvalidOperationException("Cannot detect delimiter from the bounded sample; specify a delimiter explicitly.");
            return ',';
        }
    }

    private static Encoding? GetUtf16Encoding(ReadOnlySpan<byte> sample) => sample.Length >= 2
        ? (sample[0], sample[1]) switch
        {
            (0xFF, 0xFE) => Encoding.Unicode,
            (0xFE, 0xFF) => Encoding.BigEndianUnicode,
            _ => null
        }
        : null;

    /// <summary>
    /// Infers the schema of CSV data by analyzing sample rows.
    /// </summary>
    /// <param name="data">The CSV data to analyze.</param>
    /// <param name="options">Optional inference options.</param>
    /// <returns>The inferred schema with column types and statistics.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the data is empty.</exception>
    public static CsvSchemaInferenceResult Infer(string data, CsvSchemaInferenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ReadOnlySpan<char> csv = data.AsSpan();
        int bomLength = 0;
        if (!csv.IsEmpty && csv[0] == '\uFEFF')
        {
            csv = csv[1..];
            bomLength = 1;
        }
        if (csv.IsEmpty)
            throw new InvalidOperationException("Cannot infer schema from empty data.");

        options ??= CsvSchemaInferenceOptions.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.SampleRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxColumnCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxRowSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRowSize, CsvAsyncStreamReader.ABSOLUTE_MAX_BUFFER_SIZE);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxScannedInputSize);
        var delimiter = options.Delimiter ?? DetectDelimiter(data.AsSpan(), options.MaxScannedInputSize);
        var maxSampleRows = options.SampleRows;

        long availableBudget = options.MaxScannedInputSize - bomLength;
        if (availableBudget <= 0)
            throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: false);
        bool budgetTruncated = csv.Length > availableBudget;
        if (budgetTruncated)
            csv = csv[..(int)availableBudget];

        // Parse the CSV data to extract headers and values
        var readOptions = new Core.CsvReadOptions
        {
            Delimiter = delimiter,
            MaxColumnCount = options.MaxColumnCount,
            MaxRowCount = int.MaxValue,
            MaxRowSize = options.MaxRowSize,
            AllowNewlinesInsideQuotes = true,
            TrackSourceLineNumbers = true
        };
        readOptions.Validate();
        using var reader = new CsvRowReader<char>(csv, readOptions, allowBatchScan: !budgetTruncated);

        // Read header row
        bool hasHeader;
        try
        {
            hasHeader = reader.MoveNext();
        }
        catch (CsvException ex) when (ex.QuoteStartPosition.HasValue && reader.RemainingInputLength > options.MaxRowSize)
        {
            throw CsvException.RowSizeLimitExceeded(options.MaxRowSize, isUtf8: false);
        }
        catch (CsvException ex) when (budgetTruncated && ex.QuoteStartPosition.HasValue)
        {
            throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: false);
        }
        catch (CsvException ex) when (budgetTruncated && ex.ErrorCode == CsvErrorCode.TooManyColumns &&
            !HasUnquotedLineEnding(csv, reader.RemainingInputLength) &&
            reader.RemainingInputLength > options.MaxRowSize)
        {
            throw CsvException.RowSizeLimitExceeded(options.MaxRowSize, isUtf8: false);
        }
        catch (CsvException ex) when (budgetTruncated && ex.ErrorCode == CsvErrorCode.TooManyColumns &&
            !HasUnquotedLineEnding(csv, reader.RemainingInputLength))
        {
            throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: false);
        }
        if (!hasHeader)
        {
            if (budgetTruncated)
                throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: false);
            throw new InvalidOperationException("Cannot infer schema from empty data.");
        }
        CsvRow<char> headerRow = reader.Current;
        ThrowIfRowTooLarge(headerRow.Line, options.MaxRowSize);
        ThrowIfBudgetCutsOffRow(csv, reader.RemainingInputLength, budgetTruncated, options.MaxScannedInputSize);

        int columnCount = headerRow.ColumnCount;
        var headers = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
            headers[i] = headerRow[i].ToString();

        // Track type candidates per column
        var candidates = new ColumnTypeTracker[columnCount];
        for (int i = 0; i < columnCount; i++)
            candidates[i] = new ColumnTypeTracker();

        int sampledRows = 0;
        int expectedNextLine;
        bool hasSkippedEmptyRows = false;

        try
        {
            if (headerRow.SourceLineNumber > 1)
            {
                hasSkippedEmptyRows = true;
            }
            expectedNextLine = headerRow.SourceLineNumber + CountNewlines(headerRow.Line) + 1;

            while (sampledRows < maxSampleRows && reader.MoveNext())
            {
                var row = reader.Current;
                ThrowIfRowTooLarge(row.Line, options.MaxRowSize);
                ThrowIfBudgetCutsOffRow(csv, reader.RemainingInputLength, budgetTruncated, options.MaxScannedInputSize);
                sampledRows++;

                if (row.SourceLineNumber > expectedNextLine)
                {
                    hasSkippedEmptyRows = true;
                }
                expectedNextLine = row.SourceLineNumber + CountNewlines(row.Line) + 1;

                for (int i = 0; i < Math.Min(columnCount, row.ColumnCount); i++)
                {
                    var value = row[i].ToString();
                    candidates[i].Observe(value);
                }

                // Mark missing columns as null (e.g., empty rows with fewer columns)
                for (int i = row.ColumnCount; i < columnCount; i++)
                {
                    candidates[i].Observe("");
                }
            }

            if (budgetTruncated && sampledRows < maxSampleRows)
                throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: false);

            if (!hasSkippedEmptyRows && sampledRows < maxSampleRows)
            {
                int totalLines = CountNewlines(csv) + 1;
                if (totalLines > expectedNextLine)
                {
                    hasSkippedEmptyRows = true;
                }
            }
        }
        catch (CsvException ex) when (budgetTruncated && ex.ErrorCode == CsvErrorCode.TooManyColumns &&
            !HasUnquotedLineEnding(csv, reader.RemainingInputLength) &&
            reader.RemainingInputLength > options.MaxRowSize)
        {
            throw CsvException.RowSizeLimitExceeded(options.MaxRowSize, isUtf8: false);
        }
        catch (CsvException ex) when (budgetTruncated && ex.ErrorCode == CsvErrorCode.TooManyColumns &&
            !HasUnquotedLineEnding(csv, reader.RemainingInputLength))
        {
            throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: false);
        }
        catch (CsvException ex) when (ex.ErrorCode == CsvErrorCode.ParseError && !ex.IsRowSizeLimitExceeded)
        {
            if (ex.QuoteStartPosition.HasValue && reader.RemainingInputLength > options.MaxRowSize)
                throw CsvException.RowSizeLimitExceeded(options.MaxRowSize, isUtf8: false);
            if (budgetTruncated && ex.QuoteStartPosition.HasValue)
                throw CsvException.InputSizeLimitExceeded(options.MaxScannedInputSize, isUtf8: false);
            // Gracefully stop inference and output whatever was successfully parsed.
        }

        if (hasSkippedEmptyRows)
        {
            for (int i = 0; i < columnCount; i++)
                candidates[i].HasNulls = true;
        }

        // Build results
        var columns = new CsvInferredColumn[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            columns[i] = new CsvInferredColumn(
                headers[i],
                candidates[i].GetInferredType(),
                candidates[i].HasNulls,
                candidates[i].MaxLength);
        }

        return new CsvSchemaInferenceResult(columns, sampledRows);
    }

    private static char DetectDelimiter(ReadOnlySpan<char> data, long maxScannedInputSize)
    {
        ReadOnlySpan<char> prefix = data[..(int)Math.Min(Math.Min(data.Length, DETECTION_SAMPLE_BYTES + 2), maxScannedInputSize)];
        byte[] encoded = new byte[Encoding.UTF8.GetByteCount(prefix)];
        Encoding.UTF8.GetBytes(prefix, encoded);
        int length = Math.Min(encoded.Length, DETECTION_SAMPLE_BYTES);
        bool hasMore = prefix.Length < data.Length || encoded.Length > length;
        try
        {
            return DetectSampleDelimiter(encoded.AsSpan(0, length), hasMore);
        }
        catch (InvalidOperationException) when (hasMore && prefix.Length == maxScannedInputSize)
        {
            throw CsvException.InputSizeLimitExceeded(maxScannedInputSize, isUtf8: false);
        }
    }

    private static void ThrowIfRowTooLarge(ReadOnlySpan<char> row, int maxRowSize)
    {
        if (row.Length > maxRowSize)
            throw CsvException.RowSizeLimitExceeded(maxRowSize, isUtf8: false);
    }

    private static void ThrowIfBudgetCutsOffRow(
        ReadOnlySpan<char> csv, int remainingInputLength, bool budgetTruncated, long maxScannedInputSize)
    {
        if (budgetTruncated && remainingInputLength == 0 && csv[^1] is not ('\r' or '\n'))
            throw CsvException.InputSizeLimitExceeded(maxScannedInputSize, isUtf8: false);
    }

    private static bool HasUnquotedLineEnding(ReadOnlySpan<char> csv, int remainingInputLength)
    {
        ReadOnlySpan<char> row = csv[^remainingInputLength..];
        bool inQuotes = false;
        for (int i = 0; i < row.Length; i++)
        {
            if (row[i] == '"')
            {
                if (inQuotes && i + 1 < row.Length && row[i + 1] == '"')
                    i++;
                else
                    inQuotes = !inQuotes;
            }
            else if (!inQuotes && (row[i] == '\r' || row[i] == '\n'))
            {
                return true;
            }
        }
        return false;
    }

    private static int CountNewlines(ReadOnlySpan<char> span)
    {
        int count = 0;
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (c == '\n')
            {
                count++;
            }
            else if (c == '\r')
            {
                count++;
                if (i + 1 < span.Length && span[i + 1] == '\n')
                {
                    i++; // Skip LF of CRLF to count it as one newline
                }
            }
        }
        return count;
    }


    private sealed class ColumnTypeTracker
    {
        private bool hasIntegers;
        private bool hasLongs;
        private bool hasDecimals;
        private bool hasBooleans;
        private bool hasDateTimes;
        private bool hasGuids;
        private bool hasStrings;
        private int nonEmptyCount;

        public bool HasNulls { get; set; }
        public int MaxLength { get; private set; }

        public void Observe(string value)
        {
            if (value.Length > MaxLength)
                MaxLength = value.Length;

            if (string.IsNullOrEmpty(value))
            {
                HasNulls = true;
                return;
            }

            nonEmptyCount++;

            // Try types from most specific to least
            if (bool.TryParse(value, out _))
            {
                hasBooleans = true;
                return;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                hasIntegers = true;
                return;
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                hasLongs = true;
                return;
            }

            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                hasDecimals = true;
                return;
            }

            if (Guid.TryParse(value, out _))
            {
                hasGuids = true;
                return;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                hasDateTimes = true;
                return;
            }

            hasStrings = true;
        }

        public CsvInferredType GetInferredType()
        {
            if (nonEmptyCount == 0)
                return CsvInferredType.String;

            // If any value is string, the whole column is string
            if (hasStrings)
                return CsvInferredType.String;

            // Boolean: only booleans
            if (hasBooleans && !hasIntegers && !hasLongs && !hasDecimals && !hasGuids && !hasDateTimes)
                return CsvInferredType.Boolean;

            // Guid: only guids
            if (hasGuids && !hasIntegers && !hasLongs && !hasDecimals && !hasBooleans && !hasDateTimes)
                return CsvInferredType.Guid;

            // DateTime: only datetimes
            if (hasDateTimes && !hasIntegers && !hasLongs && !hasDecimals && !hasBooleans && !hasGuids)
                return CsvInferredType.DateTime;

            // Numeric hierarchy: int < long < decimal
            bool hasAnyNumeric = hasIntegers || hasLongs || hasDecimals;
            if (hasAnyNumeric && !hasBooleans && !hasGuids && !hasDateTimes)
            {
                if (hasDecimals)
                    return CsvInferredType.Decimal;
                if (hasLongs)
                    return CsvInferredType.Long;
                return CsvInferredType.Integer;
            }

            // Mixed types fall back to String
            return CsvInferredType.String;
        }
    }
}
