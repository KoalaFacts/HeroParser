using HeroParser.Configuration;
using HeroParser.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.Core;

/// <summary>
/// High-performance static CSV parser for reading CSV data with minimal allocations.
/// </summary>
public static class CsvParser
{
    /// <summary>
    /// Parses CSV content from a string using default configuration.
    /// </summary>
    /// <param name="csv">The CSV content to parse.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when csv is null.</exception>
    /// <exception cref="CsvParseException">Thrown when parsing fails due to malformed CSV.</exception>
    public static string[][] Parse(string csv)
    {
        return Parse(csv, CsvConfiguration.Default);
    }

    /// <summary>
    /// Parses CSV content from a string using the specified configuration.
    /// </summary>
    /// <param name="csv">The CSV content to parse.</param>
    /// <param name="configuration">The configuration to use for parsing.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when csv or configuration is null.</exception>
    /// <exception cref="CsvParseException">Thrown when parsing fails due to malformed CSV.</exception>
    public static string[][] Parse(string csv, CsvConfiguration configuration)
    {
        if (csv == null)
            throw new ArgumentNullException(nameof(csv));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        configuration.Validate();

        var result = new List<string[]>();
        var reader = new StringReader(csv);

        using (reader)
        {
            ParseInternal(reader, configuration, result);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Parses CSV content from a TextReader using default configuration.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    /// <exception cref="CsvParseException">Thrown when parsing fails due to malformed CSV.</exception>
    public static string[][] Parse(TextReader reader)
    {
        return Parse(reader, CsvConfiguration.Default);
    }

    /// <summary>
    /// Parses CSV content from a TextReader using the specified configuration.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="configuration">The configuration to use for parsing.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader or configuration is null.</exception>
    /// <exception cref="CsvParseException">Thrown when parsing fails due to malformed CSV.</exception>
    public static string[][] Parse(TextReader reader, CsvConfiguration configuration)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        configuration.Validate();

        var result = new List<string[]>();
        ParseInternal(reader, configuration, result);
        return result.ToArray();
    }

    /// <summary>
    /// Asynchronously parses CSV content from a TextReader using default configuration.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous parse operation containing an array of string arrays.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    /// <exception cref="CsvParseException">Thrown when parsing fails due to malformed CSV.</exception>
    public static Task<string[][]> ParseAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        return ParseAsync(reader, CsvConfiguration.Default, cancellationToken);
    }

    /// <summary>
    /// Asynchronously parses CSV content from a TextReader using the specified configuration.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <param name="configuration">The configuration to use for parsing.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous parse operation containing an array of string arrays.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader or configuration is null.</exception>
    /// <exception cref="CsvParseException">Thrown when parsing fails due to malformed CSV.</exception>
    public static async Task<string[][]> ParseAsync(TextReader reader, CsvConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        configuration.Validate();

        return await Task.Run(() =>
        {
            var result = new List<string[]>();
            ParseInternal(reader, configuration, result);
            return result.ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ParseInternal(TextReader reader, CsvConfiguration config, List<string[]> result)
    {
        var fields = new List<string>();
        var currentField = new List<char>();
        bool inQuotes = false;
        bool skipHeader = config.HasHeaderRow;
        long lineNumber = 1;
        int position = 0;

        int ch;
        while ((ch = reader.Read()) != -1)
        {
            position++;
            char c = (char)ch;

            try
            {
                if (c == '\r')
                {
                    continue; // Skip CR, handle LF
                }

                if (c == '\n')
                {
                    if (!inQuotes)
                    {
                        // End of line
                        AddField(currentField, fields, config);

                        if (fields.Count > 0 || !config.IgnoreEmptyLines)
                        {
                            if (!skipHeader)
                            {
                                result.Add(fields.ToArray());
                            }
                            skipHeader = false;
                        }

                        fields.Clear();
                        currentField.Clear();
                        lineNumber++;
                        position = 0;
                        continue;
                    }
                    else
                    {
                        // Newline within quoted field
                        currentField.Add(c);
                        lineNumber++;
                        position = 0;
                        continue;
                    }
                }

                if (c == config.Quote)
                {
                    if (inQuotes)
                    {
                        // Check for escaped quote
                        int nextCh = reader.Peek();
                        if (nextCh == config.Escape && config.Escape == config.Quote)
                        {
                            reader.Read(); // Consume the escape character
                            currentField.Add(c);
                            position++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        if (currentField.Count > 0 && config.StrictMode)
                        {
                            throw new CsvParseException($"Unexpected quote character at position {position}", lineNumber, position, null);
                        }
                        inQuotes = true;
                    }
                    continue;
                }

                if (c == config.Delimiter && !inQuotes)
                {
                    AddField(currentField, fields, config);
                    continue;
                }

                currentField.Add(c);
            }
            catch (CsvParseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CsvParseException($"Unexpected error at line {lineNumber}, position {position}", lineNumber, position, null, ex);
            }
        }

        // Handle final field and row
        if (inQuotes && config.StrictMode)
        {
            throw new CsvParseException($"Unterminated quoted field at end of input", lineNumber, position, null);
        }

        AddField(currentField, fields, config);

        // Only add the final row if it has content or if we're not ignoring empty lines
        bool hasContent = fields.Count > 1 || (fields.Count == 1 && !string.IsNullOrEmpty(fields[0]));
        if (hasContent || !config.IgnoreEmptyLines)
        {
            if (!skipHeader)
            {
                result.Add(fields.ToArray());
            }
        }
    }

    private static void AddField(List<char> currentField, List<string> fields, CsvConfiguration config)
    {
        var fieldValue = new string(currentField.ToArray());

        if (config.TrimValues)
        {
            fieldValue = fieldValue.Trim();
        }

        fields.Add(fieldValue);
        currentField.Clear();
    }
}