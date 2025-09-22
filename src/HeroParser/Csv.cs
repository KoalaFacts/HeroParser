using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Configuration;
using HeroParser.Core;

namespace HeroParser;

/// <summary>
/// Main entry point for CSV parsing operations providing a clean, intuitive API.
/// </summary>
public static class Csv
{
    // Parse methods for immediate parsing to arrays

    /// <summary>
    /// Parses CSV content from a string.
    /// </summary>
    /// <param name="csvContent">The CSV content to parse.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <returns>An array of data records (header row excluded if HasHeaderRow is true).</returns>
    /// <exception cref="ArgumentNullException">Thrown when csvContent is null.</exception>
    /// <example>
    /// <code>
    /// var data = Csv.Parse("Name,Age\nJohn,25\nJane,30");
    /// // Returns: [["John", "25"], ["Jane", "30"]]
    /// </code>
    /// </example>
    public static string[][] Parse(string csvContent, CsvReadConfiguration? configuration = null)
    {
        if (csvContent == null)
            throw new ArgumentNullException(nameof(csvContent));

        var config = (configuration ?? CsvReadConfiguration.Default) with { StringContent = csvContent };
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
    public static string[][] Parse(TextReader reader, CsvReadConfiguration? configuration = null)
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
    public static string[][] Parse(Stream stream, Encoding? encoding = null, CsvReadConfiguration? configuration = null)
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

    // Async methods

    /// <summary>
    /// Asynchronously parses CSV content from a TextReader.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="configuration">Optional configuration settings. If null, uses RFC 4180 compliant defaults.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation with an array of data records.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public static async Task<string[][]> ParseAsync(TextReader reader, CsvReadConfiguration? configuration = null, CancellationToken cancellationToken = default)
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
    public static async Task<string[][]> ParseAsync(Stream stream, Encoding? encoding = null, CsvReadConfiguration? configuration = null, CancellationToken cancellationToken = default)
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

    // Reader creation methods for advanced scenarios

    /// <summary>
    /// Creates a CSV reader for streaming/iterative processing.
    /// Use this when you need to process large files without loading all data into memory.
    /// </summary>
    /// <param name="configuration">The configuration for the CSV reader.</param>
    /// <returns>A new CSV reader instance that must be disposed after use.</returns>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid or has no data source.</exception>
    /// <example>
    /// <code>
    /// var config = CsvReadConfiguration.FromFile(@"C:\large-file.csv");
    /// using var reader = Csv.CreateReader(config);
    ///
    /// while (!reader.EndOfCsv)
    /// {
    ///     var record = reader.ReadRecord();
    ///     if (record != null)
    ///         ProcessRecord(record);
    /// }
    /// </code>
    /// </example>
    public static ICsvReader CreateReader(CsvReadConfiguration configuration)
    {
        configuration.Validate();
        return new CsvReader(configuration);
    }

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
}