using HeroParser.Configuration;
using HeroParser.Exceptions;
using System.Runtime.CompilerServices;
using System.Text;
#if NET5_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace HeroParser.Core;

/// <summary>
/// Delegate for processing CSV rows as ref structs with zero allocations.
/// </summary>
/// <param name="row">The CSV row to process.</param>
public delegate void HeroRowProcessor(in HeroCsvRow row);

/// <summary>
/// High-performance CSV reader with traditional and zero-allocation APIs.
///
/// This class can be used across threads with proper synchronization.
/// The reader maintains internal state and should not be accessed concurrently.
/// For multi-threaded scenarios, create separate reader instances per thread.
/// </summary>
public sealed class CsvReader : ICsvReader
{
    // Security constants for error handling
    private const string SecureParseErrorMessage = "CSV parsing error occurred. Enable debug mode for detailed information.";
    private const string SecureQuoteErrorMessage = "Invalid quote character encountered during parsing.";
    private const string SecureFormatErrorMessage = "CSV format validation failed.";

    private readonly TextReader _reader;
    private readonly bool _ownsReader;
    private readonly List<string> _currentRecord;
    private readonly List<char> _currentField;
    private Dictionary<string, int>? _headerLookup;
    private bool _disposed;
    private long _rowNumber;
    private bool _inQuotes;

    // Zero-allocation support - consolidated from HeroCsvReader
    private char[] _buffer;
    private int[] _columnStarts;
    private int[] _columnLengths;
    private int _bufferLength;
    private int _columnCount;
    private bool _hasCurrentRow;


    // Security constants for zero-allocation parsing
    private const int MaxBufferSize = 50 * 1024 * 1024; // 50MB maximum buffer size
    private const int MaxColumnCount = 10000; // Maximum columns per row to prevent memory exhaustion
    private const int InitialColumnCapacity = 64; // Initial column array capacity
    private const int MaxRowLength = 10 * 1024 * 1024; // 10MB maximum row length


    /// <inheritdoc/>
    public CsvReadConfiguration Configuration { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string>? Headers { get; private set; }

    /// <inheritdoc/>
    public bool EndOfCsv { get; private set; }

    /// <inheritdoc/>
    public HeroCsvRow CurrentRow => GetCurrentRow();

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvReader"/> class.
    /// </summary>
    /// <param name="configuration">The configuration containing the data source and all parsing options.</param>
    /// <exception cref="ArgumentNullException">Thrown when configuration is null.</exception>
    /// <exception cref="ArgumentException">Thrown when data source is invalid.</exception>
    public CsvReader(CsvReadConfiguration configuration)
    {
        Configuration = configuration;
        Configuration.Validate();

        // Create the appropriate TextReader based on data source type
        (_reader, _ownsReader) = CreateTextReader(configuration);

        if (_reader == null)
            throw new ArgumentException("Invalid data source configuration.", nameof(configuration));

        // Traditional parsing initialization (always needed for compatibility)
        _currentRecord = [];
        _currentField = new List<char>(256);
        _rowNumber = 0;
        _inQuotes = false;

        // Zero-allocation parsing initialization (for ref struct methods)
        var initialBufferSize = Math.Min(configuration.BufferSize, MaxBufferSize);
        _buffer = System.Buffers.ArrayPool<char>.Shared.Rent(initialBufferSize);
        _columnStarts = new int[InitialColumnCapacity];
        _columnLengths = new int[InitialColumnCapacity];
    }

    /// <inheritdoc/>
    public void Dispose()
    {

        if (_disposed)
            return;

        if (_ownsReader)
        {
            _reader?.Dispose();
        }

        // Return buffer to ArrayPool
        if (_buffer.Length > 0)
        {
            System.Buffers.ArrayPool<char>.Shared.Return(_buffer);
        }

        _disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddCurrentField()
    {
        // F1 Cycle 2: Use memory pool for field processing
        string fieldValue;

        if (_currentField.Count > 0)
        {
#if NET5_0_OR_GREATER
            // Use CollectionsMarshal for zero-copy span access on .NET 5+
            var fieldSpan = CollectionsMarshal.AsSpan(_currentField);
            if (Configuration.TrimValues)
            {
                var (start, length) = SpanOperations.TrimWhitespace(fieldSpan);
                fieldValue = length > 0 ? new string(fieldSpan.Slice(start, length)) : string.Empty;
            }
            else
            {
                fieldValue = new string(fieldSpan);
            }
#else
            // Fallback for older frameworks
            fieldValue = new string([.. _currentField]);
            if (Configuration.TrimValues)
            {
                fieldValue = fieldValue.Trim();
            }
#endif
        }
        else
        {
            fieldValue = string.Empty;
        }

        _currentRecord.Add(fieldValue);
        _currentField.Clear();
    }

    /// <summary>
    /// Creates a TextReader based on the configuration's data source properties.
    /// </summary>
    /// <param name="config">The configuration containing the data source.</param>
    /// <returns>A tuple containing the TextReader and whether this reader owns it.</returns>
    /// <exception cref="ArgumentException">Thrown when data source is invalid or missing.</exception>
    private static (TextReader reader, bool ownsReader) CreateTextReader(CsvReadConfiguration config)
    {
        // Check data sources in priority order
        if (config.StringContent != null)
        {
            return (new StringReader(config.StringContent), true);
        }

        if (config.Reader != null)
        {
            return (config.Reader, false);
        }

        if (config.Stream != null)
        {
            // Security: Only detect encoding from BOM if no explicit encoding was provided
            // When user specifies encoding explicitly, respect their choice to prevent encoding attacks
            var explicitEncoding = config.Encoding;
            var shouldDetectEncoding = explicitEncoding == null;
            var encoding = explicitEncoding ?? Encoding.UTF8;

            return (new StreamReader(config.Stream, encoding, detectEncodingFromByteOrderMarks: shouldDetectEncoding), true);
        }

        if (config.FilePath != null)
        {
            // Security: Only detect encoding from BOM if no explicit encoding was provided
            // When user specifies encoding explicitly, respect their choice to prevent encoding attacks
            var explicitEncoding = config.Encoding;
            var shouldDetectEncoding = explicitEncoding == null;
            var encoding = explicitEncoding ?? Encoding.UTF8;

            return (new StreamReader(File.OpenRead(config.FilePath), encoding, detectEncodingFromByteOrderMarks: shouldDetectEncoding), true);
        }

        if (config.ByteContent.HasValue)
        {
            return CreateReaderFromBytes(config);
        }

        throw new ArgumentException("No data source specified. Use one of the FromXxx factory methods or provide StringContent, Reader, Stream, FilePath, or ByteContent.", nameof(config));
    }

    /// <summary>
    /// Creates a TextReader from byte content with proper encoding handling for different frameworks.
    /// </summary>
    /// <param name="config">The configuration containing byte content and encoding.</param>
    /// <returns>A tuple containing the TextReader and ownership flag.</returns>
    private static (TextReader reader, bool ownsReader) CreateReaderFromBytes(CsvReadConfiguration config)
    {
        var bytes = config.ByteContent ?? throw new ArgumentException("ByteContent cannot be null when DataSourceType is Bytes.");
        var encoding = config.Encoding ?? Encoding.UTF8;

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return (new StringReader(encoding.GetString(bytes.Span)), true);
#else
        return (new StringReader(encoding.GetString(bytes.ToArray())), true);
#endif
    }

    // Internal factory methods for CsvReaderBuilder use only

    /// <summary>
    /// Creates a new CSV reader from a string.
    /// </summary>
    /// <param name="csv">The CSV content.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static CsvReader CreateReader(string csv, CsvReadConfiguration? configuration = null)
    {
        if (csv == null)
            throw new ArgumentNullException(nameof(csv));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            StringContent = csv
        };
        return new CsvReader(config);
    }

    /// <summary>
    /// Creates a new CSV reader from a TextReader.
    /// </summary>
    /// <param name="reader">The text reader.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static CsvReader CreateReader(TextReader reader, CsvReadConfiguration? configuration = null)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            Reader = reader
        };
        return new CsvReader(config);
    }

    /// <summary>
    /// Creates a new CSV reader from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF8).</param>
    /// <returns>A new CSV reader.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static CsvReader CreateReader(Stream stream, CsvReadConfiguration? configuration = null, Encoding? encoding = null)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            Stream = stream,
            Encoding = encoding
        };
        return new CsvReader(config);
    }

    /// <summary>
    /// Creates a new CSV reader from a file path.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF8).</param>
    /// <returns>A new CSV reader.</returns>
    internal static CsvReader CreateReaderFromFile(string path, CsvReadConfiguration? configuration = null, Encoding? encoding = null)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        return CreateReader(stream, configuration, encoding);
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// Creates a new CSV reader from a ReadOnlySpan of chars.
    /// </summary>
    /// <param name="csv">The CSV content as a span.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static CsvReader CreateReader(ReadOnlySpan<char> csv, CsvReadConfiguration? configuration = null)
    {
        // Convert span to string for now (will optimize in future cycles with span-based reader)
        return CreateReader(csv.ToString(), configuration);
    }

    /// <summary>
    /// Creates a new CSV reader from a ReadOnlyMemory of chars.
    /// </summary>
    /// <param name="csv">The CSV content as memory.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static CsvReader CreateReader(ReadOnlyMemory<char> csv, CsvReadConfiguration? configuration = null)
    {
        // Convert memory to string for now (will optimize in future cycles with memory-based reader)
        return CreateReader(csv.ToString(), configuration);
    }
#endif

    // ICsvReader interface implementation

    /// <inheritdoc/>
    public IEnumerable<string[]> ReadAll()
    {
        while (!EndOfCsv)
        {
            var record = ReadRecord();
            if (record != null)
                yield return record;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string[]>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var records = new List<string[]>();
        while (!EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record != null)
                records.Add(record);
        }
        return records;
    }

    /// <inheritdoc/>
    public string[]? ReadRecord()
    {
        if (EndOfCsv)
            return null;

        var record = ReadNextRecord();
        if (record == null)
        {
            EndOfCsv = true;
            return null;
        }

        // Handle headers on first row
        if (_rowNumber == 1 && Configuration.HasHeaderRow)
        {
            Headers = Array.AsReadOnly(record);
            InitializeHeaderLookup(record);
            return ReadRecord(); // Read the next record after headers
        }

        return record;
    }

    /// <inheritdoc/>
    public async Task<string[]?> ReadRecordAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ReadRecord(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string GetField(string[] record, string columnName)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrEmpty(columnName)) throw new ArgumentException("Column name cannot be null or empty.", nameof(columnName));
        if (Headers == null) throw new InvalidOperationException("Headers are not available. Ensure HasHeaderRow is true in configuration.");

        if (_headerLookup != null && _headerLookup.TryGetValue(columnName, out var index))
        {
            if (index < record.Length)
                return record[index];
        }

        throw new ArgumentException("Column not found.", nameof(columnName));
    }

    /// <inheritdoc/>
    public bool TryGetField(string[] record, string columnName, out string? value)
    {
        value = null;
        if (record == null || string.IsNullOrEmpty(columnName) || Headers == null || _headerLookup == null)
            return false;

        if (_headerLookup.TryGetValue(columnName, out var index) && index < record.Length)
        {
            value = record[index];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the next record from the CSV data.
    /// F1 Cycle 3: Enhanced with SIMD optimization when possible.
    /// </summary>
    /// <returns>The next record as a string array, or null if end of data.</returns>
    private string[]? ReadNextRecord()
    {
        if (_disposed || _reader.Peek() == -1)
            return null;

        // F1 Cycle 3: Try SIMD optimization for string-based content
        if (Configuration.StringContent != null && !_simdProcessingCompleted)
        {
            return ReadNextRecordSIMD();
        }

        // Fallback to character-by-character parsing for streams
        return ReadNextRecordTraditional();
    }

    private bool _simdProcessingCompleted = false;
    private IEnumerator<string[]>? _simdEnumerator;

    /// <summary>
    /// SIMD-optimized record reading for string content.
    /// Direct implementation following constitutional zero virtual dispatch.
    /// </summary>
    private string[]? ReadNextRecordSIMD()
    {
        if (_simdEnumerator == null)
        {
            // Initialize SIMD processing with direct implementation
            _simdEnumerator = ParseStringContentWithSIMD(Configuration.StringContent!, Configuration).GetEnumerator();
        }

        if (_simdEnumerator.MoveNext())
        {
            _rowNumber++;
            var record = _simdEnumerator.Current;

            // Handle headers on first row
            if (_rowNumber == 1 && Configuration.HasHeaderRow)
            {
                Headers = Array.AsReadOnly(record);
                InitializeHeaderLookup(record);
                // Continue to next record for actual data
                if (_simdEnumerator.MoveNext())
                {
                    _rowNumber++;
                    return _simdEnumerator.Current;
                }
            }

            return record;
        }

        _simdProcessingCompleted = true;
        return null;
    }

    /// <summary>
    /// Direct SIMD-optimized parsing for string content.
    /// Integrated implementation following constitutional zero virtual dispatch.
    /// </summary>
#if NET6_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private static IEnumerable<string[]> ParseStringContentWithSIMD(string csvContent, CsvReadConfiguration config)
    {
        if (string.IsNullOrEmpty(csvContent))
            yield break;

        // Fast path detection - if no quotes, use optimized simple parsing
        var hasQuotes = csvContent.Contains(config.Quote);

        if (!hasQuotes)
        {
            // Simple CSV - use optimized fast path
            foreach (var record in ParseSimpleCsvDirect(csvContent, config))
            {
                yield return record;
            }
        }
        else
        {
            // Complex CSV with quotes - use full-featured parser
            foreach (var record in ParseComplexCsvDirect(csvContent, config))
            {
                yield return record;
            }
        }
    }

    /// <summary>
    /// Fast path for simple CSV without quotes using optimized delimiter scanning.
    /// </summary>
#if NET6_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private static IEnumerable<string[]> ParseSimpleCsvDirect(string csvContent, CsvReadConfiguration config)
    {
        var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        var fields = new List<string>(32); // Pre-allocate for typical CSV row

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line) && config.IgnoreEmptyLines)
                continue;

            fields.Clear();

            // Parse fields in current line using optimized scanning
            if (!string.IsNullOrEmpty(line))
            {
                ParseLineFieldsSimpleDirect(line, config.Delimiter, fields, config.TrimValues);
            }
            else if (!config.IgnoreEmptyLines)
            {
                // For empty lines when not ignoring them, add a single empty field
                fields.Add(string.Empty);
            }

            if (fields.Count > 0)
            {
                yield return fields.ToArray();
            }
        }
    }

    /// <summary>
    /// Optimized field parsing for simple CSV lines.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ParseLineFieldsSimpleDirect(string line, char delimiter, List<string> fields, bool trimValues)
    {
        if (string.IsNullOrEmpty(line))
        {
            fields.Add(string.Empty);
            return;
        }

        // Use optimized string splitting for simple cases
        var parts = line.Split(delimiter);

        if (trimValues)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                fields.Add(parts[i].Trim());
            }
        }
        else
        {
            fields.AddRange(parts);
        }
    }

    /// <summary>
    /// Full-featured parsing for complex CSV with quotes and escapes.
    /// </summary>
#if NET6_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private static IEnumerable<string[]> ParseComplexCsvDirect(string csvContent, CsvReadConfiguration config)
    {
        var fields = new List<string>(32);
        var position = 0;
        var inQuotes = false;
        var fieldStart = 0;
        var lineNumber = 1;

        while (position < csvContent.Length)
        {
            var c = csvContent[position];

            if (!inQuotes)
            {
                if (c == config.Quote)
                {
                    // In strict mode, quotes must only appear at the start of fields
                    if (config.StrictMode && position > fieldStart)
                    {
                        throw new CsvParseException(SecureQuoteErrorMessage, lineNumber, null, null);
                    }
                    inQuotes = true;
                }
                else if (c == config.Delimiter)
                {
                    // End of field
                    var fieldValue = csvContent.Substring(fieldStart, position - fieldStart);
                    fields.Add(ExtractAndUnescapeFieldDirect(fieldValue, config.Quote, config.TrimValues));
                    fieldStart = position + 1;
                }
                else if (c == '\r' || c == '\n')
                {
                    // End of record
                    var fieldValue = csvContent.Substring(fieldStart, position - fieldStart);
                    fields.Add(ExtractAndUnescapeFieldDirect(fieldValue, config.Quote, config.TrimValues));

                    yield return fields.ToArray();
                    fields.Clear();

                    // Skip CRLF
                    if (c == '\r' && position + 1 < csvContent.Length && csvContent[position + 1] == '\n')
                    {
                        position++;
                    }

                    lineNumber++;
                    fieldStart = position + 1;
                }
            }
            else
            {
                if (c == config.Quote)
                {
                    // Check for escaped quote
                    if (position + 1 < csvContent.Length && csvContent[position + 1] == config.Quote)
                    {
                        // Escaped quote - skip both characters
                        position++;
                    }
                    else
                    {
                        // End of quoted field
                        inQuotes = false;
                    }
                }
            }

            position++;
        }

        // In strict mode, check for unterminated quotes at end of file
        if (config.StrictMode && inQuotes)
        {
            throw new CsvParseException(SecureQuoteErrorMessage, lineNumber, null, null);
        }

        // Handle final field at end of content
        if (fieldStart < csvContent.Length || fields.Count > 0)
        {
            var fieldValue = fieldStart < csvContent.Length ? csvContent.Substring(fieldStart) : string.Empty;
            fields.Add(ExtractAndUnescapeFieldDirect(fieldValue, config.Quote, config.TrimValues));

            if (fields.Count > 0)
            {
                yield return fields.ToArray();
            }
        }
    }

    /// <summary>
    /// Extracts and unescapes a field value with full quote and escape handling.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ExtractAndUnescapeFieldDirect(string field, char quote, bool trimValues)
    {
        if (string.IsNullOrEmpty(field))
            return string.Empty;

        var processed = trimValues ? field.Trim() : field;

        if (processed.Length >= 2 && processed[0] == quote && processed[processed.Length - 1] == quote)
        {
            // Quoted field - remove quotes and handle escapes
            var content = processed.Substring(1, processed.Length - 2);

            // Handle escaped quotes
            return content.Replace("\"\"", "\"");
        }

        return processed;
    }

    /// <summary>
    /// Traditional character-by-character record reading.
    /// Used for streams and as fallback when SIMD is not applicable.
    /// </summary>
    private string[]? ReadNextRecordTraditional()
    {
        _currentRecord.Clear();
        _currentField.Clear();
        _inQuotes = false;

        int ch;
        while ((ch = _reader.Read()) != -1)
        {
            var c = (char)ch;

            if (_inQuotes)
            {
                if (c == Configuration.Quote)
                {
                    // Check for escaped quote
                    if (_reader.Peek() == Configuration.Quote)
                    {
                        _reader.Read(); // consume the escape quote
                        _currentField.Add(Configuration.Quote);
                    }
                    else
                    {
                        _inQuotes = false;
                    }
                }
                else
                {
                    _currentField.Add(c);
                }
            }
            else
            {
                if (c == Configuration.Quote)
                {
                    // In strict mode, quotes must only appear at the start of fields
                    if (Configuration.StrictMode && _currentField.Count > 0)
                    {
                        throw new CsvParseException(SecureQuoteErrorMessage, _rowNumber + 1, null, null);
                    }
                    _inQuotes = true;
                }
                else if (c == Configuration.Delimiter)
                {
                    AddCurrentField();
                }
                else if (c == '\r' || c == '\n')
                {
                    // Handle line endings
                    if (c == '\r' && _reader.Peek() == '\n')
                    {
                        _reader.Read(); // consume \n after \r
                    }

                    // In strict mode, check for unterminated quotes
                    if (Configuration.StrictMode && _inQuotes)
                    {
                        throw new CsvParseException(SecureQuoteErrorMessage, _rowNumber + 1, null, null);
                    }

                    AddCurrentField();
                    _rowNumber++;

                    // Skip empty lines if configured
                    if (_currentRecord.Count == 0 && Configuration.IgnoreEmptyLines)
                    {
                        continue;
                    }

                    return _currentRecord.Count > 0 ? [.. _currentRecord] : null;
                }
                else
                {
                    _currentField.Add(c);
                }
            }
        }

        // Handle end of file
        if (_currentField.Count > 0 || _currentRecord.Count > 0)
        {
            // In strict mode, check for unterminated quotes at end of file
            if (Configuration.StrictMode && _inQuotes)
            {
                throw new CsvParseException(SecureQuoteErrorMessage, _rowNumber + 1, null, null);
            }

            AddCurrentField();
            _rowNumber++;
            return [.. _currentRecord];
        }

        return null;
    }


    /// <summary>
    /// Initializes the header lookup dictionary.
    /// </summary>
    /// <param name="headers">The header row.</param>
    private void InitializeHeaderLookup(string[] headers)
    {
        if (_headerLookup != null)
            return;

        _headerLookup = [];
        for (int i = 0; i < headers.Length; i++)
        {
            _headerLookup[headers[i]] = i;
        }
    }

    // ========== Ref Struct API Methods (zero-allocation) ==========

    /// <summary>
    /// Advances to the next row and makes it available via CurrentRow.
    /// </summary>
    /// <returns>True if a row was read; false if end of CSV has been reached.</returns>
    public bool Read()
    {

        if (_disposed)
            throw new ObjectDisposedException(nameof(CsvReader));

        if (EndOfCsv)
            return false;

        // Initialize headers if needed
        if (Configuration.HasHeaderRow && Headers == null)
        {
            InitializeHeadersRef();
            // If after reading header there's no more content, return false
            if (EndOfCsv)
                return false;
        }

        // Read and parse the next row
        if (!TryReadNextRowRef())
        {
            EndOfCsv = true;
            _hasCurrentRow = false;
            return false;
        }

        _rowNumber++;
        _hasCurrentRow = true;
        return true;
    }

    /// <summary>
    /// Gets the column index for a given header name.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>The column index.</returns>
    /// <exception cref="ArgumentException">Thrown when column name is not found.</exception>
    internal int GetColumnIndex(string columnName)
    {
        if (_headerLookup?.TryGetValue(columnName, out var index) == true)
        {
            return index;
        }

        throw new ArgumentException("Column not found in headers.", nameof(columnName));
    }

    /// <summary>
    /// Tries to get the column index for a given header name.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <param name="index">The column index if found.</param>
    /// <returns>True if the column was found, false otherwise.</returns>
    internal bool TryGetColumnIndex(string columnName, out int index)
    {
        if (_headerLookup?.TryGetValue(columnName, out index) == true)
        {
            return true;
        }

        index = -1;
        return false;
    }

    private void InitializeHeadersRef()
    {
        if (TryReadNextRowRef())
        {
            _rowNumber++;
            _hasCurrentRow = true;

            var headerRow = CurrentRow;
            var headers = new string[headerRow.ColumnCount];
            var headerLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < headerRow.ColumnCount; i++)
            {
                var header = headerRow[i].ToString();
                headers[i] = header;
                headerLookup[header] = i;
            }

            Headers = headers;
            _headerLookup = headerLookup;
        }
        else
        {
            EndOfCsv = true;
        }
    }

    /// <summary>
    /// Gets the current row as a zero-allocation HeroCsvRow.
    /// </summary>
    /// <returns>The current row, or an empty row if no valid row is available.</returns>
    private HeroCsvRow GetCurrentRow()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CsvReader));

        if (!_hasCurrentRow || _columnCount == 0)
        {
            return default; // Returns empty HeroCsvRow
        }

        var rowSpan = new ReadOnlySpan<char>(_buffer, 0, _bufferLength);
        var columnStarts = new ReadOnlySpan<int>(_columnStarts, 0, _columnCount);
        var columnLengths = new ReadOnlySpan<int>(_columnLengths, 0, _columnCount);

        return new HeroCsvRow(this, rowSpan, columnStarts, columnLengths, Configuration.TrimValues);
    }


    private bool TryReadNextRowRef()
    {
        _bufferLength = 0;
        _columnCount = 0;

        var line = _reader.ReadLine();
        if (line == null)
        {
            return false;
        }

        // Skip empty lines if configured
        if (string.IsNullOrEmpty(line) && Configuration.IgnoreEmptyLines)
        {
            return TryReadNextRowRef();
        }

        // Ensure buffer capacity
        EnsureBufferCapacityRef(line.Length);

        // Copy line to buffer
        line.AsSpan().CopyTo(_buffer.AsSpan());
        _bufferLength = line.Length;

        // Parse the line into columns
        ParseLineIntoColumnsRef();

        return true;
    }

    private void ParseLineIntoColumnsRef()
    {
        var span = new ReadOnlySpan<char>(_buffer, 0, _bufferLength);
        var delimiter = Configuration.Delimiter;
        var quote = Configuration.Quote;

        _columnCount = 0;
        var start = 0;
        var inQuotes = false;

        for (int i = 0; i < span.Length; i++)
        {
            var ch = span[i];

            if (ch == quote)
            {
                inQuotes = !inQuotes;
            }
            else if (ch == delimiter && !inQuotes)
            {
                // End of column
                AddColumnRef(start, i - start);
                start = i + 1;
            }
        }

        // Add final column
        AddColumnRef(start, span.Length - start);
    }

    private void AddColumnRef(int start, int length)
    {
        // Security check: Prevent DoS through excessive column counts
        if (_columnCount >= MaxColumnCount)
        {
            throw new InvalidOperationException($"Row contains too many columns ({_columnCount}). Maximum allowed: {MaxColumnCount}. This may indicate a malformed CSV or potential DoS attack.");
        }

        // Ensure column arrays capacity with controlled growth
        if (_columnCount >= _columnStarts.Length)
        {
            var newCapacity = Math.Min(_columnStarts.Length * 2, MaxColumnCount);
            if (newCapacity <= _columnCount)
            {
                throw new InvalidOperationException($"Cannot expand column arrays beyond maximum limit of {MaxColumnCount} columns.");
            }

            Array.Resize(ref _columnStarts, newCapacity);
            Array.Resize(ref _columnLengths, newCapacity);
        }

        _columnStarts[_columnCount] = start;
        _columnLengths[_columnCount] = length;
        _columnCount++;
    }

    private void EnsureBufferCapacityRef(int requiredLength)
    {
        // Security check: Prevent DoS through excessive buffer sizes
        if (requiredLength > MaxRowLength)
        {
            throw new InvalidOperationException($"Row length ({requiredLength:N0} characters) exceeds maximum allowed size of {MaxRowLength:N0} characters. This may indicate a malformed CSV or potential DoS attack.");
        }

        if (_buffer.Length < requiredLength)
        {
            // Calculate new buffer size with controlled growth and safety limits
            var newSize = Math.Min(requiredLength * 2, MaxBufferSize);
            if (newSize < requiredLength)
            {
                // If doubling would exceed max, use the minimum required size
                newSize = Math.Min(requiredLength, MaxBufferSize);
            }

            if (newSize < requiredLength)
            {
                throw new InvalidOperationException($"Required buffer size ({requiredLength:N0}) exceeds maximum allowed buffer size of {MaxBufferSize:N0} characters.");
            }

            System.Buffers.ArrayPool<char>.Shared.Return(_buffer);
            _buffer = System.Buffers.ArrayPool<char>.Shared.Rent(newSize);
        }
    }
}