using System.Runtime.CompilerServices;
using System.Text;

namespace HeroParser.Configuration;


/// <summary>
/// Configuration options for CSV reading operations.
/// </summary>
public readonly record struct CsvReadConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CsvReadConfiguration"/> struct.
    /// </summary>
    public CsvReadConfiguration()
    {
    }

    /// <summary>
    /// Gets the default CSV read configuration with RFC 4180 compliant settings.
    /// </summary>
    public static CsvReadConfiguration Default { get; } = new();

    /// <summary>
    /// Gets the field delimiter character. Default is comma (',').
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Gets the quote character used to enclose fields containing special characters. Default is double quote ('"').
    /// </summary>
    public char Quote { get; init; } = '"';

    /// <summary>
    /// Gets the escape character used to escape quotes within quoted fields. Default is double quote ('"').
    /// </summary>
    public char Escape { get; init; } = '"';

    /// <summary>
    /// Gets a value indicating whether the first row contains column headers. Default is true.
    /// </summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to ignore empty lines. Default is true.
    /// </summary>
    public bool IgnoreEmptyLines { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to trim whitespace from field values. Default is false.
    /// </summary>
    public bool TrimValues { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether parsing should be strict (RFC 4180 compliant). Default is true.
    /// </summary>
    public bool StrictMode { get; init; } = true;

    /// <summary>
    /// Gets the buffer size in bytes for reading operations. Default is 64KB.
    /// </summary>
    public int BufferSize { get; init; } = 65536; // 64KB

    /// <summary>
    /// Gets a value indicating whether to allow jagged arrays (rows with different column counts). Default is false.
    /// </summary>
    public bool AllowJaggedRows { get; init; } = false;

    /// <summary>
    /// Gets the string content when DataSourceType is String.
    /// </summary>
    public string? StringContent { get; init; }

    /// <summary>
    /// Gets the TextReader when DataSourceType is TextReader.
    /// </summary>
    public TextReader? Reader { get; init; }

    /// <summary>
    /// Gets the Stream when DataSourceType is Stream.
    /// </summary>
    public Stream? Stream { get; init; }

    /// <summary>
    /// Gets the file path when DataSourceType is FilePath.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Gets the byte content when DataSourceType is Bytes.
    /// </summary>
    public ReadOnlyMemory<byte>? ByteContent { get; init; }

    /// <summary>
    /// Gets the encoding to use for byte or stream sources. Default is UTF-8.
    /// </summary>
    public Encoding? Encoding { get; init; }

    /// <summary>
    /// Creates a new configuration instance with custom delimiter.
    /// </summary>
    /// <param name="delimiter">The field delimiter character.</param>
    /// <returns>A new <see cref="CsvReadConfiguration"/> instance.</returns>
    public static CsvReadConfiguration WithDelimiter(char delimiter) => Default with { Delimiter = delimiter };

    /// <summary>
    /// Creates a new configuration instance for tab-separated values.
    /// </summary>
    /// <returns>A new <see cref="CsvReadConfiguration"/> instance configured for TSV.</returns>
    public static CsvReadConfiguration ForTsv() => Default with { Delimiter = '\t' };

    /// <summary>
    /// Creates a new configuration instance for semicolon-separated values.
    /// </summary>
    /// <returns>A new <see cref="CsvReadConfiguration"/> instance configured for SSV.</returns>
    public static CsvReadConfiguration ForSsv() => Default with { Delimiter = ';' };

    /// <summary>
    /// Creates a new configuration instance for string content.
    /// </summary>
    /// <param name="content">The CSV content as a string.</param>
    /// <returns>A new <see cref="CsvReadConfiguration"/> instance.</returns>
    public static CsvReadConfiguration FromString(string content) => Default with
    {
        StringContent = content
    };

    /// <summary>
    /// Creates a new configuration instance for a TextReader.
    /// </summary>
    /// <param name="reader">The TextReader to read CSV content from.</param>
    /// <returns>A new <see cref="CsvReadConfiguration"/> instance.</returns>
    public static CsvReadConfiguration FromReader(TextReader reader) => Default with
    {
        Reader = reader
    };

    /// <summary>
    /// Creates a new configuration instance for a Stream.
    /// </summary>
    /// <param name="stream">The Stream to read CSV content from.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF-8).</param>
    /// <returns>A new <see cref="CsvReadConfiguration"/> instance.</returns>
    public static CsvReadConfiguration FromStream(Stream stream, Encoding? encoding = null) => Default with
    {
        Stream = stream,
        Encoding = encoding ?? System.Text.Encoding.UTF8
    };

    /// <summary>
    /// Creates a new configuration instance for a file path.
    /// </summary>
    /// <param name="filePath">The path to the CSV file.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF-8).</param>
    /// <returns>A new <see cref="CsvReadConfiguration"/> instance.</returns>
    public static CsvReadConfiguration FromFile(string filePath, Encoding? encoding = null) => Default with
    {
        FilePath = filePath,
        Encoding = encoding ?? System.Text.Encoding.UTF8
    };

    /// <summary>
    /// Creates a new configuration instance for byte content.
    /// </summary>
    /// <param name="bytes">The CSV content as bytes.</param>
    /// <param name="encoding">The encoding to use (defaults to UTF-8).</param>
    /// <returns>A new <see cref="CsvReadConfiguration"/> instance.</returns>
    public static CsvReadConfiguration FromBytes(ReadOnlyMemory<byte> bytes, Encoding? encoding = null) => Default with
    {
        ByteContent = bytes,
        Encoding = encoding ?? System.Text.Encoding.UTF8
    };

    /// <summary>
    /// Validates the configuration settings and throws an exception if any setting is invalid.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration contains invalid settings.</exception>
    public readonly void Validate()
    {
        ValidateBufferSize(BufferSize);
        ValidateCharacters(Delimiter, Quote, Escape);
    }

    /// <summary>
    /// Validates that the buffer size is valid.
    /// </summary>
    /// <param name="value">The buffer size to validate.</param>
    /// <param name="paramName">The parameter name (automatically provided).</param>
    /// <exception cref="ArgumentException">Thrown when buffer size is invalid.</exception>
    private static void ValidateBufferSize(int value, [CallerArgumentExpression(nameof(value))] string paramName = "")
    {
        if (value <= 0)
            throw new ArgumentException($"Buffer size must be greater than zero. Got: {value}", paramName);
    }

    /// <summary>
    /// Validates that delimiter, quote, and escape characters are compatible.
    /// </summary>
    /// <param name="delimiter">The delimiter character.</param>
    /// <param name="quote">The quote character.</param>
    /// <param name="escape">The escape character.</param>
    private static void ValidateCharacters(char delimiter, char quote, char escape)
    {
        if (delimiter == quote)
            throw new ArgumentException($"Delimiter ('{delimiter}') and quote characters cannot be the same.", nameof(quote));

        if (delimiter == escape && escape != quote)
            throw new ArgumentException($"Delimiter ('{delimiter}') and escape ('{escape}') characters cannot be the same unless escape equals quote.", nameof(escape));
    }

}