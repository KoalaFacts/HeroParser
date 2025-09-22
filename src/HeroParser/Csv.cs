using HeroParser.Configuration;
using HeroParser.Core;
using System.Text;

namespace HeroParser;

/// <summary>
/// Main entry point for CSV parsing operations providing a clean, intuitive API.
/// </summary>
public static partial class Csv
{
    /// <summary>
    /// Creates a fluent builder for configuring CSV reading options.
    /// </summary>
    /// <returns>A new CSV reader builder instance.</returns>
    /// <example>
    /// <code>
    /// using var reader = Csv.Configure()
    ///     .ForContent(csvString)
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
    /// Creates a new CSV reader from a string for advanced streaming scenarios.
    /// Returns a CsvReader instance that must be disposed after use.
    /// This method is for power users who need direct access to the reader for custom processing.
    /// </summary>
    /// <param name="content">The CSV content.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader that must be disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
    /// <example>
    /// <code>
    /// using var reader = Csv.CreateReader(csvContent);
    /// while (!reader.EndOfCsv)
    /// {
    ///     var record = reader.ReadRecord();
    ///     if (record != null)
    ///     {
    ///         // Custom processing with full control over iteration
    ///         ProcessRecord(record);
    ///     }
    /// }
    /// </code>
    /// </example>
    public static CsvReader CreateReader(string content, CsvReadConfiguration? configuration = null)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = content };
        return new CsvReader(config);
    }

    /// <summary>
    /// Creates a new CSV reader from a TextReader for advanced streaming scenarios.
    /// Returns a CsvReader instance that must be disposed after use.
    /// </summary>
    /// <param name="reader">The text reader.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader that must be disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public static CsvReader CreateReader(TextReader reader, CsvReadConfiguration? configuration = null)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        var config = (configuration ?? CsvReadConfiguration.Default) with { Reader = reader };
        return new CsvReader(config);
    }

    /// <summary>
    /// Creates a new CSV reader from a stream for advanced streaming scenarios.
    /// Returns a CsvReader instance that must be disposed after use.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF8).</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader that must be disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static CsvReader CreateReader(Stream stream, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            Stream = stream,
            Encoding = encoding ?? Encoding.UTF8
        };
        return new CsvReader(config);
    }

    /// <summary>
    /// Creates a new CSV reader from a file path for advanced streaming scenarios.
    /// Returns a CsvReader instance that must be disposed after use.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF8).</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader that must be disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public static CsvReader CreateReaderFromFile(string filePath, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            FilePath = filePath,
            Encoding = encoding ?? Encoding.UTF8
        };
        return new CsvReader(config);
    }

    /// <summary>
    /// Creates a new CSV reader from a byte array for advanced streaming scenarios.
    /// Returns a CsvReader instance that must be disposed after use.
    /// </summary>
    /// <param name="bytes">The CSV content as a byte array.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader that must be disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when bytes is null.</exception>
    public static CsvReader CreateReader(byte[] bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = encoding ?? Encoding.UTF8
        };
        return new CsvReader(config);
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// Creates a new CSV reader from a ReadOnlySpan of chars for advanced streaming scenarios.
    /// Returns a CsvReader instance that must be disposed after use.
    /// </summary>
    /// <param name="span">The CSV content as a span.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader that must be disposed.</returns>
    public static CsvReader CreateReader(ReadOnlySpan<char> span, CsvReadConfiguration? configuration = null)
    {
        // Convert span to string for now (will optimize in future cycles with span-based reader)
        var content = span.ToString();
        var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = content };
        return new CsvReader(config);
    }

    /// <summary>
    /// Creates a new CSV reader from a ReadOnlyMemory of chars for advanced streaming scenarios.
    /// Returns a CsvReader instance that must be disposed after use.
    /// </summary>
    /// <param name="memory">The CSV content as memory.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader that must be disposed.</returns>
    public static CsvReader CreateReader(ReadOnlyMemory<char> memory, CsvReadConfiguration? configuration = null)
    {
        // Convert memory to string for now (will optimize in future cycles with memory-based reader)
        var content = memory.ToString();
        var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = content };
        return new CsvReader(config);
    }

    /// <summary>
    /// Creates a new CSV reader from a ReadOnlyMemory of bytes for advanced streaming scenarios.
    /// Returns a CsvReader instance that must be disposed after use.
    /// </summary>
    /// <param name="bytes">The CSV content as ReadOnlyMemory of bytes.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration.</param>
    /// <returns>A new CSV reader that must be disposed.</returns>
    public static CsvReader CreateReader(ReadOnlyMemory<byte> bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = encoding ?? Encoding.UTF8
        };
        return new CsvReader(config);
    }
#endif
}