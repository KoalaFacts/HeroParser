using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Configuration;
using HeroParser.Exceptions;

namespace HeroParser.Core;

/// <summary>
/// High-performance CSV reader implementation.
/// </summary>
public sealed class CsvReader : ICsvReader
{
    private readonly TextReader _reader;
    private readonly bool _ownsReader;
    private readonly List<string> _currentRecord;
    private readonly List<char> _currentField;
    private Dictionary<string, int>? _headerLookup;
    private bool _disposed;
    private long _rowNumber;
    private bool _inQuotes;

    /// <inheritdoc/>
    public CsvReadConfiguration Configuration { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string>? Headers { get; private set; }

    /// <inheritdoc/>
    public bool EndOfCsv { get; private set; }

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
        _currentRecord = [];
        _currentField = new List<char>(256);
        _rowNumber = 0;
        _inQuotes = false;
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

        _disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddCurrentField()
    {
        var fieldValue = new string(_currentField.ToArray());

        if (Configuration.TrimValues)
        {
            fieldValue = fieldValue.Trim();
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
            return (new StreamReader(config.Stream, config.Encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true), true);
        }

        if (config.FilePath != null)
        {
            return (new StreamReader(File.OpenRead(config.FilePath), config.Encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true), true);
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

        throw new ArgumentException($"Column '{columnName}' not found.", nameof(columnName));
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
    /// </summary>
    /// <returns>The next record as a string array, or null if end of data.</returns>
    private string[]? ReadNextRecord()
    {
        if (_disposed || _reader.Peek() == -1)
            return null;

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
                        throw new CsvParseException($"Unescaped quote character found at line {_rowNumber + 1}. In strict mode, quotes must only appear at the start of fields or be properly escaped.", _rowNumber + 1, null, null);
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
                        throw new CsvParseException($"Unterminated quote found at line {_rowNumber + 1}. In strict mode, all quotes must be properly closed.", _rowNumber + 1, null, null);
                    }

                    AddCurrentField();
                    _rowNumber++;

                    // Skip empty lines if configured
                    if (_currentRecord.Count == 0 && Configuration.IgnoreEmptyLines)
                    {
                        continue;
                    }

                    return _currentRecord.Count > 0 ? _currentRecord.ToArray() : null;
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
                throw new CsvParseException($"Unterminated quote found at end of file (line {_rowNumber + 1}). In strict mode, all quotes must be properly closed.", _rowNumber + 1, null, null);
            }

            AddCurrentField();
            _rowNumber++;
            return _currentRecord.ToArray();
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
}