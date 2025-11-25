namespace HeroParser.SeparatedValues;

/// <summary>
/// Configures how HeroParser writes CSV data.
/// </summary>
/// <remarks>
/// The defaults follow RFC 4180 conventions.
/// </remarks>
public sealed record CsvWriterOptions
{
    /// <summary>
    /// Gets or sets the field delimiter character (comma by default).
    /// </summary>
    /// <remarks>Delimiters must be ASCII (0-127).</remarks>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Gets or sets the quote character used to escape delimiters inside a field (double quote by default).
    /// </summary>
    /// <remarks>The value must be ASCII and cannot match <see cref="Delimiter"/>.</remarks>
    public char Quote { get; init; } = '"';

    /// <summary>
    /// Gets or sets the line terminator sequence (CRLF by default for RFC 4180 compliance).
    /// </summary>
    /// <remarks>Common values are "\r\n" (CRLF), "\n" (LF), or "\r" (CR).</remarks>
    public string NewLine { get; init; } = "\r\n";

    /// <summary>
    /// Gets or sets a value indicating whether all fields should be quoted, regardless of content.
    /// </summary>
    /// <remarks>When false (default), only fields containing delimiters, quotes, or newlines are quoted.</remarks>
    public bool AlwaysQuote { get; init; } = false;

    /// <summary>
    /// Gets a singleton representing the default configuration.
    /// </summary>
    /// <remarks>Equivalent to <c>new CsvWriterOptions()</c>.</remarks>
    public static CsvWriterOptions Default { get; } = new();

    /// <summary>
    /// Validates the option set and throws when an invalid value is detected.
    /// </summary>
    /// <exception cref="CsvException">Thrown when any property falls outside the supported range.</exception>
    internal void Validate()
    {
        if (Delimiter > 127)
        {
            throw new CsvException(
                CsvErrorCode.InvalidDelimiter,
                $"Delimiter '{Delimiter}' (U+{(int)Delimiter:X4}) must be ASCII (0-127)");
        }

        if (Quote > 127)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"Quote '{Quote}' (U+{(int)Quote:X4}) must be ASCII (0-127)");
        }

        if (Delimiter == Quote)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"Delimiter and Quote cannot be the same character ('{Delimiter}')");
        }

        if (string.IsNullOrEmpty(NewLine))
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                "NewLine cannot be null or empty");
        }
    }
}
