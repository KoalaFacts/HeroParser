using System;

namespace HeroParser.Configuration;

/// <summary>
/// Configuration options for CSV parsing and reading operations.
/// </summary>
public sealed class CsvConfiguration
{
    /// <summary>
    /// Gets the default CSV configuration with RFC 4180 compliant settings.
    /// </summary>
    public static CsvConfiguration Default { get; } = new CsvConfiguration();

    /// <summary>
    /// Gets or sets the field delimiter character. Default is comma (',').
    /// </summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>
    /// Gets or sets the quote character used to enclose fields containing special characters. Default is double quote ('"').
    /// </summary>
    public char Quote { get; set; } = '"';

    /// <summary>
    /// Gets or sets the escape character used to escape quotes within quoted fields. Default is double quote ('"').
    /// </summary>
    public char Escape { get; set; } = '"';

    /// <summary>
    /// Gets or sets a value indicating whether the first row contains column headers. Default is true.
    /// </summary>
    public bool HasHeaderRow { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to ignore empty lines. Default is true.
    /// </summary>
    public bool IgnoreEmptyLines { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to trim whitespace from field values. Default is false.
    /// </summary>
    public bool TrimValues { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether parsing should be strict (RFC 4180 compliant). Default is true.
    /// </summary>
    public bool StrictMode { get; set; } = true;

    /// <summary>
    /// Gets or sets the buffer size in bytes for reading operations. Default is 64KB.
    /// </summary>
    public int BufferSize { get; set; } = 65536; // 64KB

    /// <summary>
    /// Gets or sets a value indicating whether to allow jagged arrays (rows with different column counts). Default is false.
    /// </summary>
    public bool AllowJaggedRows { get; set; } = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvConfiguration"/> class with default RFC 4180 settings.
    /// </summary>
    public CsvConfiguration()
    {
    }

    /// <summary>
    /// Creates a new configuration instance with custom delimiter.
    /// </summary>
    /// <param name="delimiter">The field delimiter character.</param>
    /// <returns>A new <see cref="CsvConfiguration"/> instance.</returns>
    public static CsvConfiguration WithDelimiter(char delimiter)
    {
        return new CsvConfiguration { Delimiter = delimiter };
    }

    /// <summary>
    /// Creates a new configuration instance for tab-separated values.
    /// </summary>
    /// <returns>A new <see cref="CsvConfiguration"/> instance configured for TSV.</returns>
    public static CsvConfiguration ForTsv()
    {
        return new CsvConfiguration { Delimiter = '\t' };
    }

    /// <summary>
    /// Creates a new configuration instance for semicolon-separated values.
    /// </summary>
    /// <returns>A new <see cref="CsvConfiguration"/> instance configured for SSV.</returns>
    public static CsvConfiguration ForSsv()
    {
        return new CsvConfiguration { Delimiter = ';' };
    }

    /// <summary>
    /// Creates a copy of this configuration instance.
    /// </summary>
    /// <returns>A new <see cref="CsvConfiguration"/> instance with the same settings.</returns>
    public CsvConfiguration Clone()
    {
        return new CsvConfiguration
        {
            Delimiter = Delimiter,
            Quote = Quote,
            Escape = Escape,
            HasHeaderRow = HasHeaderRow,
            IgnoreEmptyLines = IgnoreEmptyLines,
            TrimValues = TrimValues,
            StrictMode = StrictMode,
            BufferSize = BufferSize,
            AllowJaggedRows = AllowJaggedRows
        };
    }

    /// <summary>
    /// Validates the configuration settings and throws an exception if any setting is invalid.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration contains invalid settings.</exception>
    public void Validate()
    {
        if (BufferSize <= 0)
            throw new ArgumentException("Buffer size must be greater than zero.", nameof(BufferSize));

        if (Delimiter == Quote)
            throw new ArgumentException("Delimiter and quote characters cannot be the same.", nameof(Quote));

        if (Delimiter == Escape && Escape != Quote)
            throw new ArgumentException("Delimiter and escape characters cannot be the same unless escape equals quote.", nameof(Escape));
    }
}