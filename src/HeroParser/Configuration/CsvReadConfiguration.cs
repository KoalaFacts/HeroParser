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
        ValidateDataSources();
    }

    /// <summary>
    /// Validates that only one data source is specified and it's valid.
    /// </summary>
    private readonly void ValidateDataSources()
    {
        int sourceCount = 0;

        if (StringContent != null) sourceCount++;
        if (Reader != null) sourceCount++;
        if (Stream != null) sourceCount++;
        if (FilePath != null) sourceCount++;
        if (ByteContent != null) sourceCount++;

        if (sourceCount == 0)
            throw new ArgumentException("At least one data source must be specified (StringContent, Reader, Stream, FilePath, or ByteContent).");

        if (sourceCount > 1)
            throw new ArgumentException("Only one data source can be specified at a time. Multiple data sources could lead to undefined behavior.");

        // Additional security validation for file paths
        if (FilePath != null)
        {
            if (string.IsNullOrWhiteSpace(FilePath))
                throw new ArgumentException("File path cannot be null, empty, or whitespace.", nameof(FilePath));

            // Prevent potential path traversal attacks
            if (FilePath.Contains("..") || FilePath.Contains("~"))
                throw new ArgumentException("File path contains potentially dangerous path traversal characters.", nameof(FilePath));

            // Check for suspicious file extensions that could indicate script injection
            var extension = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
            string[] dangerousExtensions = { ".bat", ".cmd", ".exe", ".ps1", ".vbs", ".js", ".jar" };
            if (dangerousExtensions.Contains(extension))
                throw new ArgumentException($"File extension '{extension}' is not allowed for security reasons.", nameof(FilePath));
        }

        // Validate string content size limits
        if (StringContent != null && StringContent.Length > 50 * 1024 * 1024) // 50MB limit
            throw new ArgumentException($"String content exceeds maximum allowed size of 50MB. Size: {StringContent.Length:N0} characters.", nameof(StringContent));
    }

    /// <summary>
    /// Validates that the buffer size is within safe limits.
    /// </summary>
    /// <param name="value">The buffer size to validate.</param>
    /// <param name="paramName">The parameter name (automatically provided).</param>
    /// <exception cref="ArgumentException">Thrown when buffer size is invalid.</exception>
    private static void ValidateBufferSize(int value, [CallerArgumentExpression(nameof(value))] string paramName = "")
    {
        if (value <= 0)
            throw new ArgumentException($"Buffer size must be greater than zero. Got: {value}", paramName);

        // Add maximum buffer size limits to prevent memory exhaustion attacks
        const int maxBufferSize = 100 * 1024 * 1024; // 100MB max
        if (value > maxBufferSize)
            throw new ArgumentException($"Buffer size exceeds maximum allowed size of {maxBufferSize:N0} bytes. Got: {value:N0}", paramName);

        // Warn about very small buffers that could cause performance issues
        const int minRecommendedSize = 1024; // 1KB minimum recommended
        if (value < minRecommendedSize)
            throw new ArgumentException($"Buffer size is too small for efficient processing. Minimum recommended: {minRecommendedSize:N0} bytes. Got: {value:N0}", paramName);
    }

    /// <summary>
    /// Validates that delimiter, quote, and escape characters are compatible and secure.
    /// </summary>
    /// <param name="delimiter">The delimiter character.</param>
    /// <param name="quote">The quote character.</param>
    /// <param name="escape">The escape character.</param>
    private static void ValidateCharacters(char delimiter, char quote, char escape)
    {
        // Validate character uniqueness (critical for parsing correctness)
        if (delimiter == quote)
            throw new ArgumentException($"Delimiter ('{delimiter}') and quote characters cannot be the same.", nameof(quote));

        if (delimiter == escape && escape != quote)
            throw new ArgumentException($"Delimiter ('{delimiter}') and escape ('{escape}') characters cannot be the same unless escape equals quote.", nameof(escape));

        // Security: Prevent dangerous control characters that could enable injection attacks
        ValidateCharacterSafety(delimiter, nameof(delimiter));
        ValidateCharacterSafety(quote, nameof(quote));
        ValidateCharacterSafety(escape, nameof(escape));

        // Additional RFC 4180 compliance checks
        if (char.IsWhiteSpace(delimiter) && delimiter != '\t' && delimiter != ' ')
            throw new ArgumentException($"Delimiter character '{delimiter}' (U+{(int)delimiter:X4}) is not a recommended whitespace character for CSV parsing.", nameof(delimiter));

        // Prevent NULL character usage which can cause parsing issues
        if (delimiter == '\0' || quote == '\0' || escape == '\0')
            throw new ArgumentException("NULL character (U+0000) is not allowed as a CSV control character.", nameof(delimiter));
    }

    /// <summary>
    /// Validates that a character is safe for use in CSV parsing and not exploitable.
    /// </summary>
    /// <param name="character">The character to validate.</param>
    /// <param name="paramName">The parameter name for error reporting.</param>
    private static void ValidateCharacterSafety(char character, string paramName)
    {
        // Prevent dangerous Unicode categories
        var category = char.GetUnicodeCategory(character);

        // Block potentially dangerous control characters
        if (category == System.Globalization.UnicodeCategory.Control &&
            character != '\r' && character != '\n' && character != '\t')
        {
            throw new ArgumentException($"Control character '{character}' (U+{(int)character:X4}) is not allowed for security reasons.", paramName);
        }

        // Block format characters that could be used for injection
        if (category == System.Globalization.UnicodeCategory.Format)
        {
            throw new ArgumentException($"Format character '{character}' (U+{(int)character:X4}) is not allowed as it could enable injection attacks.", paramName);
        }

        // Block surrogate characters that could cause parsing confusion
        if (category == System.Globalization.UnicodeCategory.Surrogate)
        {
            throw new ArgumentException($"Surrogate character '{character}' (U+{(int)character:X4}) is not allowed in CSV control characters.", paramName);
        }

        // Block private use characters that could hide malicious content
        if (category == System.Globalization.UnicodeCategory.PrivateUse)
        {
            throw new ArgumentException($"Private use character '{character}' (U+{(int)character:X4}) is not allowed for security reasons.", paramName);
        }
    }

}