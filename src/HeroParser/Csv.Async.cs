using HeroParser.Configuration;
using HeroParser.Core;
using System.Text;
#if NET6_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace HeroParser;

/// <summary>
/// Main entry point for CSV parsing operations providing a clean, intuitive API.
/// </summary>
public static partial class Csv
{
    // Async methods

    /// <summary>
    /// Asynchronously parses CSV content from a string.
    /// This method is useful for CPU-intensive parsing operations where you want to avoid blocking the calling thread.
    /// </summary>
    /// <param name="content">The CSV content to parse.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation with an array of data records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
    /// <example>
    /// <code>
    /// var data = await Csv.ParseStringAsync(largeString);
    /// // Process data without blocking the UI thread
    /// </code>
    /// </example>
    public static async Task<string[][]> ParseStringAsync(string content, CsvReadConfiguration? configuration = null, CancellationToken cancellationToken = default)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        // For CPU-bound parsing, use Task.Run to move to thread pool
        return await Task.Run(() =>
        {
            var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = content };
            using var reader = new CsvReader(config);
            return reader.ReadAll().ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously parses CSV content from a file.
    /// This method performs true async I/O for optimal performance and scalability.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation with an array of data records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <example>
    /// <code>
    /// var data = await Csv.ParseFileAsync(@"C:\large-data.csv");
    /// // File reading and parsing performed asynchronously
    /// </code>
    /// </example>
    public static async Task<string[][]> ParseFileAsync(string filePath, CsvReadConfiguration? configuration = null, CancellationToken cancellationToken = default)
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        // Use async file stream for true async I/O
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        return await ParseStreamAsync(fileStream, null, configuration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously parses CSV content from a TextReader.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation with an array of data records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public static async Task<string[][]> ParseReaderAsync(TextReader reader, CsvReadConfiguration? configuration = null, CancellationToken cancellationToken = default)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        var config = (configuration ?? CsvReadConfiguration.Default) with { Reader = reader };
        using var csvReader = new CsvReader(config);
        var result = await csvReader.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return result.ToArray();
    }

    /// <summary>
    /// Asynchronously parses CSV content from a stream.
    /// </summary>
    /// <param name="stream">The stream containing CSV data.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation with an array of data records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static async Task<string[][]> ParseStreamAsync(Stream stream, Encoding? encoding = null, CsvReadConfiguration? configuration = null, CancellationToken cancellationToken = default)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            Stream = stream,
            Encoding = encoding ?? Encoding.UTF8
        };
        using var csvReader = new CsvReader(config);
        var result = await csvReader.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return result.ToArray();
    }

    /// <summary>
    /// Asynchronously parses CSV content from a byte array.
    /// </summary>
    /// <param name="bytes">The CSV content as a byte array.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation with an array of data records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when bytes is null.</exception>
    public static async Task<string[][]> ParseBytesAsync(byte[] bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null, CancellationToken cancellationToken = default)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        return await Task.Run(() =>
        {
            var config = (configuration ?? CsvReadConfiguration.Default) with
            {
                ByteContent = bytes,
                Encoding = encoding ?? Encoding.UTF8
            };
            using var reader = new CsvReader(config);
            return reader.ReadAll().ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

#if NET6_0_OR_GREATER
    // Modern async streaming methods using IAsyncEnumerable for .NET 6+

    /// <summary>
    /// Asynchronously streams CSV content from a string using IAsyncEnumerable for memory efficiency.
    /// This method enables async streaming without loading the entire dataset into memory at once.
    /// </summary>
    /// <param name="content">The CSV content to parse.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An async enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
    /// <example>
    /// <code>
    /// await foreach (var record in Csv.FromStringAsync(largeString))
    /// {
    ///     // Process record immediately without loading all data into memory
    ///     await ProcessRecordAsync(record);
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<string[]> FromStringAsync(string content, CsvReadConfiguration? configuration = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = content };
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
    /// Asynchronously streams CSV content from a file using IAsyncEnumerable.
    /// Ideal for processing large files without loading them entirely into memory.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An async enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <example>
    /// <code>
    /// await foreach (var record in Csv.FromFileAsync(@"C:\large-data.csv"))
    /// {
    ///     // Process each record as it's read from disk asynchronously
    ///     await ProcessRecordAsync(record);
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<string[]> FromFileAsync(string filePath, CsvReadConfiguration? configuration = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        var config = (configuration ?? CsvReadConfiguration.Default) with { FilePath = filePath };
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
    /// Asynchronously streams CSV content from a TextReader using IAsyncEnumerable.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An async enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public static async IAsyncEnumerable<string[]> FromReaderAsync(TextReader reader, CsvReadConfiguration? configuration = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        var config = (configuration ?? CsvReadConfiguration.Default) with { Reader = reader };
        using var csvReader = new CsvReader(config);

        while (!csvReader.EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await csvReader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record != null)
                yield return record;
        }
    }

    /// <summary>
    /// Asynchronously streams CSV content from a stream using IAsyncEnumerable.
    /// </summary>
    /// <param name="stream">The stream containing CSV data.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An async enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static async IAsyncEnumerable<string[]> FromStreamAsync(Stream stream, Encoding? encoding = null, CsvReadConfiguration? configuration = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            Stream = stream,
            Encoding = encoding ?? Encoding.UTF8
        };
        using var csvReader = new CsvReader(config);

        while (!csvReader.EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await csvReader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record != null)
                yield return record;
        }
    }

    /// <summary>
    /// Asynchronously streams CSV content from a byte array using IAsyncEnumerable.
    /// </summary>
    /// <param name="bytes">The CSV content as a byte array.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An async enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when bytes is null.</exception>
    public static async IAsyncEnumerable<string[]> FromBytesAsync(byte[] bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = encoding ?? Encoding.UTF8
        };
        using var csvReader = new CsvReader(config);

        while (!csvReader.EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await csvReader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record != null)
                yield return record;
        }
    }

    /// <summary>
    /// Asynchronously streams CSV content from a ReadOnlyMemory of bytes using IAsyncEnumerable.
    /// </summary>
    /// <param name="bytes">The CSV content as ReadOnlyMemory of bytes.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>An async enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    public static async IAsyncEnumerable<string[]> FromBytesAsync(ReadOnlyMemory<byte> bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = encoding ?? Encoding.UTF8
        };
        using var csvReader = new CsvReader(config);

        while (!csvReader.EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await csvReader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record != null)
                yield return record;
        }
    }
#endif
}