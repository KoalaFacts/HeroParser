using System;
using HeroParser.Core;

namespace HeroParser.Configuration;

/// <summary>
/// Fluent API builder for configuring CSV parser options.
/// </summary>
public sealed class CsvParserBuilder
{
    private readonly CsvConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvParserBuilder"/> class with default configuration.
    /// </summary>
    public CsvParserBuilder() : this(new CsvConfiguration())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvParserBuilder"/> class with the specified configuration.
    /// </summary>
    /// <param name="configuration">The base configuration to start with.</param>
    /// <exception cref="ArgumentNullException">Thrown when configuration is null.</exception>
    public CsvParserBuilder(CsvConfiguration configuration)
    {
        _configuration = configuration?.Clone() ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Creates a new builder instance with default configuration.
    /// </summary>
    /// <returns>A new <see cref="CsvParserBuilder"/> instance.</returns>
    public static CsvParserBuilder Create()
    {
        return new CsvParserBuilder();
    }

    /// <summary>
    /// Creates a new builder instance from an existing configuration.
    /// </summary>
    /// <param name="configuration">The configuration to start with.</param>
    /// <returns>A new <see cref="CsvParserBuilder"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when configuration is null.</exception>
    public static CsvParserBuilder FromConfiguration(CsvConfiguration configuration)
    {
        return new CsvParserBuilder(configuration);
    }

    /// <summary>
    /// Sets the field delimiter character.
    /// </summary>
    /// <param name="delimiter">The delimiter character to use.</param>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder WithDelimiter(char delimiter)
    {
        _configuration.Delimiter = delimiter;
        return this;
    }

    /// <summary>
    /// Sets the quote character.
    /// </summary>
    /// <param name="quote">The quote character to use.</param>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder WithQuote(char quote)
    {
        _configuration.Quote = quote;
        return this;
    }

    /// <summary>
    /// Sets the escape character.
    /// </summary>
    /// <param name="escape">The escape character to use.</param>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder WithEscape(char escape)
    {
        _configuration.Escape = escape;
        return this;
    }

    /// <summary>
    /// Configures whether the first row contains headers.
    /// </summary>
    /// <param name="hasHeaders">True if the first row contains headers; otherwise, false.</param>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder WithHeaders(bool hasHeaders = true)
    {
        _configuration.HasHeaderRow = hasHeaders;
        return this;
    }

    /// <summary>
    /// Configures the parser to ignore empty lines.
    /// </summary>
    /// <param name="ignoreEmptyLines">True to ignore empty lines; otherwise, false.</param>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder IgnoreEmptyLines(bool ignoreEmptyLines = true)
    {
        _configuration.IgnoreEmptyLines = ignoreEmptyLines;
        return this;
    }

    /// <summary>
    /// Configures the parser to trim whitespace from field values.
    /// </summary>
    /// <param name="trimValues">True to trim whitespace; otherwise, false.</param>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder TrimValues(bool trimValues = true)
    {
        _configuration.TrimValues = trimValues;
        return this;
    }

    /// <summary>
    /// Configures strict mode for RFC 4180 compliance.
    /// </summary>
    /// <param name="strictMode">True for strict mode; otherwise, false.</param>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder StrictMode(bool strictMode = true)
    {
        _configuration.StrictMode = strictMode;
        return this;
    }

    /// <summary>
    /// Sets the buffer size for reading operations.
    /// </summary>
    /// <param name="bufferSize">The buffer size in bytes.</param>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when bufferSize is less than or equal to zero.</exception>
    public CsvParserBuilder WithBufferSize(int bufferSize)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be greater than zero.");

        _configuration.BufferSize = bufferSize;
        return this;
    }

    /// <summary>
    /// Configures whether to allow jagged rows (rows with different column counts).
    /// </summary>
    /// <param name="allowJaggedRows">True to allow jagged rows; otherwise, false.</param>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder AllowJaggedRows(bool allowJaggedRows = true)
    {
        _configuration.AllowJaggedRows = allowJaggedRows;
        return this;
    }

    /// <summary>
    /// Configures the parser for tab-separated values (TSV).
    /// </summary>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder ForTsv()
    {
        return WithDelimiter('\t');
    }

    /// <summary>
    /// Configures the parser for semicolon-separated values (SSV).
    /// </summary>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder ForSsv()
    {
        return WithDelimiter(';');
    }

    /// <summary>
    /// Configures the parser for pipe-separated values.
    /// </summary>
    /// <returns>The current <see cref="CsvParserBuilder"/> instance for method chaining.</returns>
    public CsvParserBuilder ForPsv()
    {
        return WithDelimiter('|');
    }

    /// <summary>
    /// Builds and returns the configured <see cref="CsvConfiguration"/>.
    /// </summary>
    /// <returns>A new <see cref="CsvConfiguration"/> instance with the configured settings.</returns>
    public CsvConfiguration Build()
    {
        _configuration.Validate();
        return _configuration.Clone();
    }

    /// <summary>
    /// Builds the configuration and parses the specified CSV content.
    /// </summary>
    /// <param name="csv">The CSV content to parse.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when csv is null.</exception>
    public string[][] Parse(string csv)
    {
        var config = Build();
        return CsvParser.Parse(csv, config);
    }

    /// <summary>
    /// Builds the configuration and parses CSV content from the specified TextReader.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <returns>An array of string arrays representing the parsed CSV data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public string[][] Parse(System.IO.TextReader reader)
    {
        var config = Build();
        return CsvParser.Parse(reader, config);
    }
}