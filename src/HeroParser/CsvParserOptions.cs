namespace HeroParser;

/// <summary>
/// Options for configuring CSV parser behavior.
/// RFC 4180 compliant by default.
/// </summary>
public sealed record CsvParserOptions
{
    /// <summary>
    /// Field delimiter character. Default: comma (',').
    /// Must be ASCII (0-127) for SIMD performance.
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Quote character for RFC 4180 compliance. Default: double quote ('"').
    /// Fields containing delimiters, newlines, or quotes are enclosed in quotes.
    /// Quotes within fields are escaped by doubling ("" becomes ").
    /// Must be ASCII (0-127) for SIMD performance.
    /// </summary>
    public char Quote { get; init; } = '"';

    /// <summary>
    /// Maximum columns per row. Default: 10,000.
    /// Throws CsvException if exceeded.
    /// </summary>
    public int MaxColumns { get; init; } = 10_000;

    /// <summary>
    /// Maximum rows to parse. Default: 10,000,000.
    /// Throws CsvException if exceeded.
    /// </summary>
    public int MaxRows { get; init; } = 10_000_000;

    /// <summary>
    /// Default options: comma delimiter, double quote, 10,000 columns, 10,000,000 rows.
    /// RFC 4180 compliant.
    /// </summary>
    public static CsvParserOptions Default { get; } = new();

    /// <summary>
    /// Validates the options and throws CsvException if invalid.
    /// </summary>
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
