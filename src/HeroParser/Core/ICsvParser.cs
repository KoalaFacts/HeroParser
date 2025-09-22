using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Configuration;

namespace HeroParser.Core;

/// <summary>
/// Interface for high-performance CSV parsing operations.
/// </summary>
public interface ICsvParser
{
    /// <summary>
    /// Parses CSV content from a string.
    /// </summary>
    /// <param name="csv">The CSV content to parse.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    string[][] Parse(string csv);

    /// <summary>
    /// Parses CSV content from a string using the specified configuration.
    /// </summary>
    /// <param name="csv">The CSV content to parse.</param>
    /// <param name="configuration">The configuration to use for parsing.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    string[][] Parse(string csv, CsvConfiguration configuration);

    /// <summary>
    /// Parses CSV content from a TextReader.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    string[][] Parse(TextReader reader);

    /// <summary>
    /// Parses CSV content from a TextReader using the specified configuration.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="configuration">The configuration to use for parsing.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    string[][] Parse(TextReader reader, CsvConfiguration configuration);

    /// <summary>
    /// Asynchronously parses CSV content from a TextReader.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous parse operation containing an array of string arrays.</returns>
    Task<string[][]> ParseAsync(TextReader reader, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously parses CSV content from a TextReader using the specified configuration.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="configuration">The configuration to use for parsing.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous parse operation containing an array of string arrays.</returns>
    Task<string[][]> ParseAsync(TextReader reader, CsvConfiguration configuration, CancellationToken cancellationToken = default);
}