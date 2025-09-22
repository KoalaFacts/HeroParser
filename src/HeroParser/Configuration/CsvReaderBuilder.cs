using HeroParser.Core;

namespace HeroParser.Configuration;

/// <summary>
/// Fluent API builder for configuring and creating CSV readers.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CsvReaderBuilder"/> class with the specified configuration.
/// </remarks>
/// <param name="configuration">The base configuration to start with.</param>
/// <exception cref="ArgumentNullException">Thrown when configuration is null.</exception>
public sealed class CsvReaderBuilder(CsvReadConfiguration configuration)
{
    private string? _csvContent;
    private System.IO.TextReader? _reader;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvReaderBuilder"/> class with default configuration.
    /// </summary>
    public CsvReaderBuilder() : this(new CsvReadConfiguration())
    {
    }

    /// <summary>
    /// Creates a new builder instance with default configuration.
    /// </summary>
    /// <returns>A new <see cref="CsvReaderBuilder"/> instance.</returns>
    public static CsvReaderBuilder Create() => new();

    /// <summary>
    /// Creates a new builder instance for the specified CSV content.
    /// </summary>
    /// <param name="csv">The CSV content to parse.</param>
    /// <returns>A new <see cref="CsvReaderBuilder"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when csv is null.</exception>
    public static CsvReaderBuilder ForContent(string csv)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(csv);
#else
        if (csv == null)
            throw new ArgumentNullException(nameof(csv));
#endif

        return new CsvReaderBuilder
        {
            _csvContent = csv
        };
    }

    /// <summary>
    /// Creates a new builder instance for the specified TextReader.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <returns>A new <see cref="CsvReaderBuilder"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public static CsvReaderBuilder ForReader(System.IO.TextReader reader)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(reader);
#else
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));
#endif

        var builder = new CsvReaderBuilder();
        builder._reader = reader;
        return builder;
    }

    /// <summary>
    /// Creates a new builder instance from an existing configuration.
    /// </summary>
    /// <param name="configuration">The configuration to start with.</param>
    /// <returns>A new <see cref="CsvReaderBuilder"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when configuration is null.</exception>
    public static CsvReaderBuilder FromConfiguration(CsvReadConfiguration configuration)
    {
        return new CsvReaderBuilder(configuration);
    }

    /// <summary>
    /// Sets the field delimiter character.
    /// </summary>
    /// <param name="delimiter">The delimiter character to use.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder WithDelimiter(char delimiter)
    {
        configuration = configuration with { Delimiter = delimiter };
        return this;
    }

    /// <summary>
    /// Sets the quote character.
    /// </summary>
    /// <param name="quote">The quote character to use.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder WithQuote(char quote)
    {
        configuration = configuration with { Quote = quote };
        return this;
    }

    /// <summary>
    /// Sets the escape character.
    /// </summary>
    /// <param name="escape">The escape character to use.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder WithEscape(char escape)
    {
        configuration = configuration with { Escape = escape };
        return this;
    }

    /// <summary>
    /// Configures whether the first row contains headers.
    /// </summary>
    /// <param name="hasHeaders">True if the first row contains headers; otherwise, false.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder WithHeaders(bool hasHeaders = true)
    {
        configuration = configuration with { HasHeaderRow = hasHeaders };
        return this;
    }

    /// <summary>
    /// Configures the parser to ignore empty lines.
    /// </summary>
    /// <param name="ignoreEmptyLines">True to ignore empty lines; otherwise, false.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder IgnoreEmptyLines(bool ignoreEmptyLines = true)
    {
        configuration = configuration with { IgnoreEmptyLines = ignoreEmptyLines };
        return this;
    }

    /// <summary>
    /// Configures the parser to trim whitespace from field values.
    /// </summary>
    /// <param name="trimValues">True to trim whitespace; otherwise, false.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder TrimValues(bool trimValues = true)
    {
        configuration = configuration with { TrimValues = trimValues };
        return this;
    }

    /// <summary>
    /// Configures strict mode for RFC 4180 compliance.
    /// </summary>
    /// <param name="strictMode">True for strict mode; otherwise, false.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder StrictMode(bool strictMode = true)
    {
        configuration = configuration with { StrictMode = strictMode };
        return this;
    }

    /// <summary>
    /// Sets the buffer size for reading operations.
    /// </summary>
    /// <param name="bufferSize">The buffer size in bytes.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when bufferSize is less than or equal to zero.</exception>
    public CsvReaderBuilder WithBufferSize(int bufferSize)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be greater than zero.");

        configuration = configuration with { BufferSize = bufferSize };
        return this;
    }

    /// <summary>
    /// Configures whether to allow jagged rows (rows with different column counts).
    /// </summary>
    /// <param name="allowJaggedRows">True to allow jagged rows; otherwise, false.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder AllowJaggedRows(bool allowJaggedRows = true)
    {
        configuration = configuration with { AllowJaggedRows = allowJaggedRows };
        return this;
    }

    /// <summary>
    /// Configures the parser for tab-separated values (TSV).
    /// </summary>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder ForTsv()
    {
        return WithDelimiter('\t');
    }

    /// <summary>
    /// Configures the parser for semicolon-separated values (SSV).
    /// </summary>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder ForSsv()
    {
        return WithDelimiter(';');
    }

    /// <summary>
    /// Configures the parser for pipe-separated values.
    /// </summary>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    public CsvReaderBuilder ForPsv()
    {
        return WithDelimiter('|');
    }

    /// <summary>
    /// Sets the CSV content for this builder.
    /// </summary>
    /// <param name="csv">The CSV content to parse.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when csv is null.</exception>
    public CsvReaderBuilder WithContent(string csv)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(csv);
#else
        if (csv == null)
            throw new ArgumentNullException(nameof(csv));
#endif

        _csvContent = csv;
        return this;
    }

    /// <summary>
    /// Sets the TextReader for this builder.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <returns>The current <see cref="CsvReaderBuilder"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when reader is null.</exception>
    public CsvReaderBuilder WithReader(System.IO.TextReader reader)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(reader);
#else
        if (reader == null)
            throw new ArgumentNullException(nameof(reader));
#endif

        _reader = reader;
        return this;
    }

    /// <summary>
    /// Builds and returns a configured <see cref="ICsvReader"/> instance.
    /// </summary>
    /// <returns>A new <see cref="ICsvReader"/> instance with the configured settings.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no input source has been specified.</exception>
    public ICsvReader Build()
    {
        configuration.Validate();
        var config = configuration;

        if (_csvContent != null)
        {
            return CsvReader.CreateReader(_csvContent, config);
        }
        else if (_reader != null)
        {
            return CsvReader.CreateReader(_reader, config);
        }
        else
        {
            throw new InvalidOperationException("No input source specified. Use ForContent() or ForReader() to specify the CSV input.");
        }
    }
}