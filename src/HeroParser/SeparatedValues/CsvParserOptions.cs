namespace HeroParser.SeparatedValues;

/// <summary>
/// Configures how HeroParser interprets CSV data.
/// </summary>
/// <remarks>
/// The defaults follow RFC 4180. Use <see cref="Validate"/> to catch invalid configurations before parsing.
/// </remarks>
public sealed record CsvParserOptions
{
    /// <summary>
    /// Gets or sets the field delimiter character (comma by default).
    /// </summary>
    /// <remarks>Delimiters must be ASCII (0-127) for SIMD acceleration.</remarks>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Gets or sets the quote character used to escape delimiters inside a field (double quote by default).
    /// </summary>
    /// <remarks>The value must be ASCII and cannot match <see cref="Delimiter"/>.</remarks>
    public char Quote { get; init; } = '"';

    /// <summary>
    /// Gets or sets the maximum number of columns a row may contain (defaults to 100).
    /// </summary>
    /// <remarks>Exceeding this value raises <see cref="CsvException"/> with <see cref="CsvErrorCode.TooManyColumns"/>.</remarks>
    public int MaxColumns { get; init; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of rows to parse before aborting (defaults to 100,000).
    /// </summary>
    /// <remarks>Helps guard against malformed files with unbounded growth.</remarks>
    public int MaxRows { get; init; } = 100_000;

    /// <summary>
    /// Gets or sets a value indicating whether SIMD acceleration is used when available (enabled by default).
    /// </summary>
    /// <remarks>Disable only for diagnostics or when targeting CPUs that lack the required instructions.</remarks>
    public bool UseSimdIfAvailable { get; init; } = true;

    /// <summary>
    /// Gets a singleton representing the default configuration.
    /// </summary>
    /// <remarks>Equivalent to <c>new CsvParserOptions()</c>.</remarks>
    public static CsvParserOptions Default { get; } = new();

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

        if (MaxColumns <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxColumns must be positive, got {MaxColumns}");
        }

        if (MaxRows <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxRows must be positive, got {MaxRows}");
        }
    }
}
