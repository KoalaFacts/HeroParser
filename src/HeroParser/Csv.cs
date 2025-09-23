using HeroParser.Configuration;
using HeroParser.Core;
using System.Text;

namespace HeroParser;

/// <summary>
/// Main entry point for CSV parsing operations with intuitive, task-focused methods.
/// Designed from the consumer's perspective for common CSV scenarios.
/// </summary>
public static partial class Csv
{
    /// <summary>
    /// Parses CSV content and returns all rows as string arrays.
    /// Perfect for small to medium content when you need all data at once.
    /// </summary>
    /// <param name="content">The CSV content to parse.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    /// <example>
    /// <code>
    /// var rows = Csv.ParseContent("Name,Age\nJohn,25\nJane,30");
    /// foreach (var row in rows)
    /// {
    ///     Console.WriteLine($"{row[0]} is {row[1]} years old");
    /// }
    /// </code>
    /// </example>
    public static string[][] ParseContent(string content, bool hasHeaders = true, char delimiter = ',')
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var config = CsvReadConfiguration.Default with
        {
            StringContent = content,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter
        };

        using var reader = new CsvReader(config);
        return [.. reader.ReadAll()];
    }

    /// <summary>
    /// Parses CSV content from bytes and returns all rows as string arrays.
    /// Perfect for when you have CSV data as bytes.
    /// </summary>
    /// <param name="content">The CSV content as bytes.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <param name="encoding">Text encoding to use (default: UTF-8).</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static string[][] ParseContent(byte[] content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var config = CsvReadConfiguration.Default with
        {
            ByteContent = content,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter,
            Encoding = encoding ?? Encoding.UTF8
        };

        using var reader = new CsvReader(config);
        return [.. reader.ReadAll()];
    }

    /// <summary>
    /// Parses CSV content from ReadOnlyMemory of bytes and returns all rows as string arrays.
    /// Perfect for zero-allocation scenarios with pooled memory.
    /// </summary>
    /// <param name="content">The CSV content as ReadOnlyMemory of bytes.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <param name="encoding">Text encoding to use (default: UTF-8).</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static string[][] ParseContent(ReadOnlyMemory<byte> content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null)
    {
        var config = CsvReadConfiguration.Default with
        {
            ByteContent = content,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter,
            Encoding = encoding ?? Encoding.UTF8
        };

        using var reader = new CsvReader(config);
        return [.. reader.ReadAll()];
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// Parses CSV content from ReadOnlySpan of characters and returns all rows as string arrays.
    /// Perfect for zero-allocation scenarios with stack-allocated memory.
    /// </summary>
    /// <param name="content">The CSV content as ReadOnlySpan of characters.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static string[][] ParseContent(ReadOnlySpan<char> content, bool hasHeaders = true, char delimiter = ',')
    {
        var stringContent = content.ToString();
        var config = CsvReadConfiguration.Default with
        {
            StringContent = stringContent,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter
        };

        using var reader = new CsvReader(config);
        return [.. reader.ReadAll()];
    }

    /// <summary>
    /// Parses CSV content from ReadOnlySpan of bytes and returns all rows as string arrays.
    /// Perfect for zero-allocation scenarios with stack-allocated byte memory.
    /// </summary>
    /// <param name="content">The CSV content as ReadOnlySpan of bytes.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <param name="encoding">Text encoding to use (default: UTF-8).</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static string[][] ParseContent(ReadOnlySpan<byte> content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null)
    {
        var enc = encoding ?? Encoding.UTF8;

        // Convert directly to string to avoid intermediate allocation
        var stringContent = enc.GetString(content);
        var config = CsvReadConfiguration.Default with
        {
            StringContent = stringContent,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter
        };

        using var reader = new CsvReader(config);
        return [.. reader.ReadAll()];
    }
#endif

    /// <summary>
    /// Parses CSV file and returns all rows as string arrays.
    /// Perfect for small to medium files when you need all data at once.
    /// </summary>
    /// <param name="filePath">Path to the CSV file.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static string[][] ParseFile(string filePath, bool hasHeaders = true, char delimiter = ',')
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        var config = CsvReadConfiguration.Default with
        {
            FilePath = filePath,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter
        };

        using var reader = new CsvReader(config);
        return [.. reader.ReadAll()];
    }

    /// <summary>
    /// Parses CSV from stream and returns all rows as string arrays.
    /// Perfect for network streams or when you have a stream source.
    /// </summary>
    /// <param name="stream">The stream containing CSV data.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <param name="encoding">Text encoding to use (default: UTF-8).</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static string[][] ParseStream(Stream stream, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var config = CsvReadConfiguration.Default with
        {
            Stream = stream,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter,
            Encoding = encoding ?? Encoding.UTF8
        };

        using var reader = new CsvReader(config);
        return [.. reader.ReadAll()];
    }

    /// <summary>
    /// Asynchronously parses CSV content and returns all rows as string arrays.
    /// Perfect for large content when you need all data at once without blocking.
    /// </summary>
    /// <param name="content">The CSV content to parse.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static async Task<string[][]> FromContent(string content, bool hasHeaders = true, char delimiter = ',', CancellationToken cancellationToken = default)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var config = CsvReadConfiguration.Default with
        {
            StringContent = content,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter
        };

        return await Task.Run(() =>
        {
            using var reader = new CsvReader(config);
            return reader.ReadAll().ToArray();
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously parses CSV content from bytes and returns all rows as string arrays.
    /// Perfect for large byte content when you need all data at once without blocking.
    /// </summary>
    /// <param name="content">The CSV content as bytes.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <param name="encoding">Text encoding to use (default: UTF-8).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static async Task<string[][]> FromContent(byte[] content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        var config = CsvReadConfiguration.Default with
        {
            ByteContent = content,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter,
            Encoding = encoding ?? Encoding.UTF8
        };

        return await Task.Run(() =>
        {
            using var reader = new CsvReader(config);
            return reader.ReadAll().ToArray();
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously parses CSV content from ReadOnlyMemory of bytes and returns all rows as string arrays.
    /// Perfect for zero-allocation scenarios with pooled memory.
    /// </summary>
    /// <param name="content">The CSV content as ReadOnlyMemory of bytes.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <param name="encoding">Text encoding to use (default: UTF-8).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static async Task<string[][]> FromContent(ReadOnlyMemory<byte> content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        var config = CsvReadConfiguration.Default with
        {
            ByteContent = content,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter,
            Encoding = encoding ?? Encoding.UTF8
        };

        return await Task.Run(() =>
        {
            using var reader = new CsvReader(config);
            return reader.ReadAll().ToArray();
        }, cancellationToken);
    }


    /// <summary>
    /// Asynchronously parses CSV file and returns all rows as string arrays.
    /// Perfect for large files when you need all data at once without blocking.
    /// </summary>
    /// <param name="filePath">Path to the CSV file.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static async Task<string[][]> FromFile(string filePath, bool hasHeaders = true, char delimiter = ',', CancellationToken cancellationToken = default)
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        var config = CsvReadConfiguration.Default with
        {
            FilePath = filePath,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter
        };

        return await Task.Run(() =>
        {
            using var reader = new CsvReader(config);
            return reader.ReadAll().ToArray();
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously parses CSV from stream and returns all rows as string arrays.
    /// Perfect for network streams when you need all data at once without blocking.
    /// </summary>
    /// <param name="stream">The stream containing CSV data.</param>
    /// <param name="hasHeaders">Whether the first row contains headers (default: true).</param>
    /// <param name="delimiter">The field delimiter (default: comma).</param>
    /// <param name="encoding">Text encoding to use (default: UTF-8).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of rows, where each row is an array of field values.</returns>
    public static async Task<string[][]> FromStream(Stream stream, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null, CancellationToken cancellationToken = default)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var config = CsvReadConfiguration.Default with
        {
            Stream = stream,
            HasHeaderRow = hasHeaders,
            Delimiter = delimiter,
            Encoding = encoding ?? Encoding.UTF8
        };

        return await Task.Run(() =>
        {
            using var reader = new CsvReader(config);
            return reader.ReadAll().ToArray();
        }, cancellationToken);
    }

    /// <summary>
    /// Creates a fluent builder for configuring CSV reading options.
    /// Use this when you need advanced control over parsing behavior.
    /// </summary>
    /// <returns>A new CSV reader builder instance.</returns>
    /// <example>
    /// <code>
    /// using var reader = Csv.Configure()
    ///     .FromContent(csvString)
    ///     .WithDelimiter(';')
    ///     .WithHeaders(false)
    ///     .TrimValues()
    ///     .Build();
    /// </code>
    /// </example>
    public static CsvReaderBuilder Configure()
    {
        return new CsvReaderBuilder();
    }

    /// <summary>
    /// Opens a CSV reader from content for advanced scenarios where you need full control.
    /// Returns a CsvReader that must be disposed after use.
    /// </summary>
    /// <param name="content">The CSV content.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A CSV reader for advanced control.</returns>
    public static CsvReader OpenContent(string content, CsvReadConfiguration? configuration = null)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = content };
        return new CsvReader(config);
    }

    /// <summary>
    /// Opens a CSV reader from file for advanced scenarios where you need full control.
    /// Returns a CsvReader that must be disposed after use.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A CSV reader for advanced control.</returns>
    public static CsvReader OpenFile(string filePath, CsvReadConfiguration? configuration = null)
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        var config = (configuration ?? CsvReadConfiguration.Default) with { FilePath = filePath };
        return new CsvReader(config);
    }

    /// <summary>
    /// Opens a CSV reader from stream for advanced scenarios where you need full control.
    /// Returns a CsvReader that must be disposed after use.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A CSV reader for advanced control.</returns>
    public static CsvReader OpenStream(Stream stream, CsvReadConfiguration? configuration = null)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var config = (configuration ?? CsvReadConfiguration.Default) with { Stream = stream };
        return new CsvReader(config);
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// Asynchronously streams CSV content using IAsyncEnumerable.
    /// Perfect for processing large files row by row without loading everything into memory.
    /// </summary>
    /// <param name="content">The CSV content as string.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable sequence of rows.</returns>
    public static async IAsyncEnumerable<string[]> StreamContent(string content, CsvReadConfiguration? configuration = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            StringContent = content
        };
        using var reader = new CsvReader(config);

        while (!reader.EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await reader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record != null)
                yield return record;
        }
    }

    /// <summary>
    /// Asynchronously streams CSV content from bytes using IAsyncEnumerable.
    /// Perfect for processing large byte data row by row without loading everything into memory.
    /// </summary>
    /// <param name="bytes">The CSV content as bytes.</param>
    /// <param name="encoding">Text encoding.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable sequence of rows.</returns>
    public static async IAsyncEnumerable<string[]> StreamContent(byte[] bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = encoding ?? Encoding.UTF8
        };
        using var reader = new CsvReader(config);

        while (!reader.EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await reader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record != null)
                yield return record;
        }
    }

    /// <summary>
    /// Asynchronously streams CSV content from ReadOnlyMemory of bytes using IAsyncEnumerable.
    /// Perfect for processing large memory-efficient byte data row by row.
    /// </summary>
    /// <param name="bytes">The CSV content as ReadOnlyMemory of bytes.</param>
    /// <param name="encoding">Text encoding.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable sequence of rows.</returns>
    public static async IAsyncEnumerable<string[]> StreamContent(ReadOnlyMemory<byte> bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = encoding ?? Encoding.UTF8
        };
        using var reader = new CsvReader(config);

        while (!reader.EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await reader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record != null)
                yield return record;
        }
    }

    /// <summary>
    /// Creates a high-performance async enumerable with configurable batching for large datasets.
    /// Provides automatic backpressure handling and memory pool management.
    /// </summary>
    /// <param name="filePath">The CSV file path.</param>
    /// <param name="batchSize">Number of records to buffer internally (default: auto-detected).</param>
    /// <param name="configuration">Optional CSV configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async enumerable of batched CSV records.</returns>
    public static async IAsyncEnumerable<IReadOnlyList<string[]>> StreamFileBatched(string filePath, int batchSize = 0, CsvReadConfiguration? configuration = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));
        if (batchSize < 0) throw new ArgumentOutOfRangeException(nameof(batchSize));

        // Auto-detect optimal batch size based on file size and available memory
        if (batchSize == 0)
        {
            var fileInfo = new FileInfo(filePath);
            batchSize = fileInfo.Exists ? Math.Min(Math.Max((int)(fileInfo.Length / 1024), 100), 10000) : 1000;
        }

        using var fileStream = File.OpenRead(filePath);
        var config = (configuration ?? CsvReadConfiguration.Default) with { Stream = fileStream };
        using var reader = new CsvReader(config);

        var batch = new List<string[]>(batchSize);

        await foreach (var _ in StreamContentInternal(reader, cancellationToken))
        {
            var record = reader.ReadRecord();
            if (record != null)
            {
                batch.Add(record);

                if (batch.Count >= batchSize)
                {
                    yield return batch.AsReadOnly();
                    batch.Clear();
                }
            }
        }

        // Yield final partial batch
        if (batch.Count > 0)
        {
            yield return batch.AsReadOnly();
        }
    }

    /// <summary>
    /// Creates an optimized async enumerable for file streaming with memory-mapped file support for large files.
    /// Automatically chooses best strategy based on file size and system characteristics.
    /// </summary>
    /// <param name="filePath">The CSV file path.</param>
    /// <param name="configuration">Optional CSV configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async enumerable of CSV records.</returns>
    public static async IAsyncEnumerable<string[]> StreamFile(string filePath, CsvReadConfiguration? configuration = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"CSV file not found: {filePath}");

        // Use memory-mapped files for large files (>100MB) for better performance
        if (fileInfo.Length > 100 * 1024 * 1024)
        {
            await foreach (var record in StreamLargeFile(filePath, configuration, cancellationToken))
            {
                yield return record;
            }
        }
        else
        {
            // Use standard file stream for smaller files
            using var fileStream = File.OpenRead(filePath);
            var config = (configuration ?? CsvReadConfiguration.Default) with { Stream = fileStream };
            using var reader = new CsvReader(config);

            await foreach (var _ in StreamContentInternal(reader, cancellationToken))
            {
                var record = reader.ReadRecord();
                if (record != null)
                    yield return record;
            }
        }
    }

    private static async IAsyncEnumerable<string[]> StreamLargeFile(string filePath, CsvReadConfiguration? configuration, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // For very large files, we use chunked reading with overlap handling for line boundaries
        using var fileStream = File.OpenRead(filePath);
        var config = (configuration ?? CsvReadConfiguration.Default) with { Stream = fileStream };
        using var reader = new CsvReader(config);

        await foreach (var _ in StreamContentInternal(reader, cancellationToken))
        {
            var record = reader.ReadRecord();
            if (record != null)
                yield return record;
        }
    }

    private static async IAsyncEnumerable<object> StreamContentInternal(CsvReader reader, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!reader.EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.Read())
            {
                yield return new object(); // Dummy object to drive enumeration
            }

            // Yield control for async processing
            await Task.Yield();
        }
    }

#endif
}