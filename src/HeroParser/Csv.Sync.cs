using HeroParser.Configuration;
using HeroParser.Core;
using System.Text;

namespace HeroParser;

/// <summary>
/// Main entry point for CSV parsing operations providing a clean, intuitive API.
/// </summary>
public static partial class Csv
{
    // Streaming methods for memory-efficient parsing

    /// <summary>
    /// Parses CSV content from a string using streaming for memory efficiency.
    /// This method returns an IEnumerable that yields records as they are parsed,
    /// avoiding loading the entire dataset into memory at once.
    /// </summary>
    /// <param name="content">The CSV content to parse.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
    /// <example>
    /// <code>
    /// foreach (var record in Csv.FromString("Name,Age\nJohn,25\nJane,30"))
    /// {
    ///     // Process record immediately without loading all data into memory
    ///     Console.WriteLine($"{record[0]} is {record[1]} years old");
    /// }
    /// </code>
    /// </example>
    public static IEnumerable<string[]> FromString(string content, CsvReadConfiguration? configuration = null)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = content };
        var reader = new CsvReader(config);

        foreach (var record in reader.ReadAll())
        {
            yield return record;
        }

        reader.Dispose();
    }

    /// <summary>
    /// Parses CSV content from a TextReader using streaming for memory efficiency.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public static IEnumerable<string[]> FromReader(TextReader reader, CsvReadConfiguration? configuration = null)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        var config = (configuration ?? CsvReadConfiguration.Default) with { Reader = reader };
        var csvReader = new CsvReader(config);

        foreach (var record in csvReader.ReadAll())
        {
            yield return record;
        }

        csvReader.Dispose();
    }

    /// <summary>
    /// Parses CSV content from a file using streaming for memory efficiency.
    /// Ideal for processing large files without loading them entirely into memory.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <example>
    /// <code>
    /// foreach (var record in Csv.FromFile(@"C:\large-data.csv"))
    /// {
    ///     // Process each record as it's read from disk
    ///     ProcessRecord(record);
    /// }
    /// </code>
    /// </example>
    public static IEnumerable<string[]> FromFile(string filePath, CsvReadConfiguration? configuration = null)
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        var config = (configuration ?? CsvReadConfiguration.Default) with { FilePath = filePath };
        var reader = new CsvReader(config);

        foreach (var record in reader.ReadAll())
        {
            yield return record;
        }

        reader.Dispose();
    }

    /// <summary>
    /// Parses CSV content from a stream using streaming for memory efficiency.
    /// </summary>
    /// <param name="stream">The stream containing CSV data.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static IEnumerable<string[]> FromStream(Stream stream, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            Stream = stream,
            Encoding = encoding ?? Encoding.UTF8
        };
        var reader = new CsvReader(config);

        foreach (var record in reader.ReadAll())
        {
            yield return record;
        }

        reader.Dispose();
    }

    // Parse methods for immediate parsing to arrays

    /// <summary>
    /// Parses CSV content from a string.
    /// </summary>
    /// <param name="content">The CSV content to parse.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when content is null.</exception>
    /// <example>
    /// <code>
    /// var data = Csv.ParseString("Name,Age\nJohn,25\nJane,30");
    /// // Returns: [["John", "25"], ["Jane", "30"]]
    /// </code>
    /// </example>
    public static string[][] ParseString(string content, CsvReadConfiguration? configuration = null)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = content };
        using var reader = new CsvReader(config);
        return reader.ReadAll().ToArray();
    }

    /// <summary>
    /// Parses CSV content from a TextReader.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public static string[][] ParseReader(TextReader reader, CsvReadConfiguration? configuration = null)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));

        var config = (configuration ?? CsvReadConfiguration.Default) with { Reader = reader };
        using var csvReader = new CsvReader(config);
        return csvReader.ReadAll().ToArray();
    }

    /// <summary>
    /// Parses CSV content from a file.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when filePath is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <example>
    /// <code>
    /// var data = Csv.ParseFile(@"C:\data\employees.csv");
    /// </code>
    /// </example>
    public static string[][] ParseFile(string filePath, CsvReadConfiguration? configuration = null)
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        var config = (configuration ?? CsvReadConfiguration.Default) with { FilePath = filePath };
        using var reader = new CsvReader(config);
        return reader.ReadAll().ToArray();
    }

    /// <summary>
    /// Parses CSV content from a stream.
    /// </summary>
    /// <param name="stream">The stream containing CSV data.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    public static string[][] ParseStream(Stream stream, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            Stream = stream,
            Encoding = encoding ?? Encoding.UTF8
        };
        using var reader = new CsvReader(config);
        return reader.ReadAll().ToArray();
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    // High-performance Parse methods for zero-allocation scenarios

    /// <summary>
    /// Parses CSV content from a ReadOnlySpan of characters.
    /// This method enables zero-allocation parsing for stack-allocated or pooled memory scenarios.
    /// </summary>
    /// <param name="span">The CSV content as a ReadOnlySpan of characters.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <example>
    /// <code>
    /// Span&lt;char&gt; buffer = stackalloc char[1024];
    /// // Fill buffer from some source
    /// var data = Csv.ParseSpan(buffer.AsReadOnly());
    /// // Zero heap allocations for the input parsing
    /// </code>
    /// </example>
    public static string[][] ParseSpan(ReadOnlySpan<char> span, CsvReadConfiguration? configuration = null)
    {
        // Convert span to string for now - future optimization opportunity
        // TODO: Implement span-based reader to avoid this allocation
        var content = span.ToString();
        var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = content };
        using var reader = new CsvReader(config);
        return reader.ReadAll().ToArray();
    }

    /// <summary>
    /// Parses CSV content from a ReadOnlyMemory of characters.
    /// This method enables efficient parsing from pooled memory without allocation.
    /// </summary>
    /// <param name="memory">The CSV content as ReadOnlyMemory of characters.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <example>
    /// <code>
    /// using var pooledBuffer = MemoryPool&lt;char&gt;.Shared.Rent(bufferSize);
    /// // Fill pooledBuffer.Memory from some source
    /// var data = Csv.ParseMemory(pooledBuffer.Memory);
    /// // Efficient parsing from pooled memory
    /// </code>
    /// </example>
    public static string[][] ParseMemory(ReadOnlyMemory<char> memory, CsvReadConfiguration? configuration = null)
    {
        return ParseSpan(memory.Span, configuration);
    }

    /// <summary>
    /// Parses CSV content from a ReadOnlySpan of characters using streaming for memory efficiency.
    /// Combines zero-allocation input with streaming output for maximum performance.
    /// </summary>
    /// <param name="csvSpan">The CSV content as a ReadOnlySpan of characters.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    public static IEnumerable<string[]> FromSpan(ReadOnlySpan<char> csvSpan, CsvReadConfiguration? configuration = null)
    {
        // Convert span to string immediately - can't yield with span parameter
        var csvContent = csvSpan.ToString();
        return FromString(csvContent, configuration);
    }

    /// <summary>
    /// Parses CSV content from a ReadOnlyMemory of characters using streaming for memory efficiency.
    /// </summary>
    /// <param name="csvMemory">The CSV content as ReadOnlyMemory of characters.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    public static IEnumerable<string[]> FromMemory(ReadOnlyMemory<char> csvMemory, CsvReadConfiguration? configuration = null)
    {
        return FromSpan(csvMemory.Span, configuration);
    }
#endif

    // Byte array parsing methods for zero-allocation scenarios

    /// <summary>
    /// Parses CSV content from a byte array.
    /// This method enables direct parsing from byte sources without intermediate string allocation.
    /// </summary>
    /// <param name="bytes">The CSV content as a byte array.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when bytes is null.</exception>
    /// <example>
    /// <code>
    /// byte[] data = File.ReadAllBytes("data.csv");
    /// var result = Csv.ParseBytes(data, Encoding.UTF8);
    /// // Direct parsing from bytes without string allocation
    /// </code>
    /// </example>
    public static string[][] ParseBytes(byte[] bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = encoding ?? Encoding.UTF8
        };
        using var reader = new CsvReader(config);
        return reader.ReadAll().ToArray();
    }

    /// <summary>
    /// Parses CSV content from a byte array using streaming for memory efficiency.
    /// </summary>
    /// <param name="bytes">The CSV content as a byte array.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when bytes is null.</exception>
    public static IEnumerable<string[]> FromBytes(byte[] bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = encoding ?? Encoding.UTF8
        };
        var reader = new CsvReader(config);

        foreach (var record in reader.ReadAll())
        {
            yield return record;
        }

        reader.Dispose();
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// Parses CSV content from a ReadOnlySpan of bytes.
    /// This method enables zero-allocation parsing from byte spans for maximum performance.
    /// </summary>
    /// <param name="bytes">The CSV content as a ReadOnlySpan of bytes.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[1024];
    /// // Fill buffer from some source
    /// var data = Csv.ParseBytes(buffer.AsReadOnly(), Encoding.UTF8);
    /// // Zero heap allocations for the input parsing
    /// </code>
    /// </example>
    public static string[][] ParseBytes(ReadOnlySpan<byte> bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        var enc = encoding ?? Encoding.UTF8;
        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes.ToArray(), // TODO: Optimize to avoid ToArray() allocation
            Encoding = enc
        };
        using var reader = new CsvReader(config);
        return reader.ReadAll().ToArray();
    }

    /// <summary>
    /// Parses CSV content from a ReadOnlyMemory of bytes.
    /// This method enables efficient parsing from pooled byte memory without allocation.
    /// </summary>
    /// <param name="bytes">The CSV content as ReadOnlyMemory of bytes.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <example>
    /// <code>
    /// using var pooledBuffer = MemoryPool&lt;byte&gt;.Shared.Rent(bufferSize);
    /// // Fill pooledBuffer.Memory from some source
    /// var data = Csv.ParseBytes(pooledBuffer.Memory, Encoding.UTF8);
    /// // Efficient parsing from pooled memory
    /// </code>
    /// </example>
    public static string[][] ParseBytes(ReadOnlyMemory<byte> bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        var enc = encoding ?? Encoding.UTF8;
        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = enc
        };
        using var reader = new CsvReader(config);
        return reader.ReadAll().ToArray();
    }

    /// <summary>
    /// Parses CSV content from a ReadOnlySpan of bytes using streaming for memory efficiency.
    /// </summary>
    /// <param name="bytes">The CSV content as a ReadOnlySpan of bytes.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    public static IEnumerable<string[]> FromBytes(ReadOnlySpan<byte> bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        // Convert span to array immediately - can't yield with span parameter
        var bytesArray = bytes.ToArray();
        return FromBytes(bytesArray, encoding, configuration);
    }

    /// <summary>
    /// Parses CSV content from a ReadOnlyMemory of bytes using streaming for memory efficiency.
    /// </summary>
    /// <param name="bytes">The CSV content as ReadOnlyMemory of bytes.</param>
    /// <param name="encoding">The text encoding to use. If null, UTF-8 is used.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An enumerable sequence of data records (header row excluded if HasHeaderRow is true).</returns>
    public static IEnumerable<string[]> FromBytes(ReadOnlyMemory<byte> bytes, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
    {
        var enc = encoding ?? Encoding.UTF8;
        var config = (configuration ?? CsvReadConfiguration.Default) with
        {
            ByteContent = bytes,
            Encoding = enc
        };
        var reader = new CsvReader(config);

        foreach (var record in reader.ReadAll())
        {
            yield return record;
        }

        reader.Dispose();
    }
#endif
}