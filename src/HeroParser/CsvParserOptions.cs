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
    /// Enable eager column parsing. Default: false (lazy parsing).
    /// When true, columns are parsed immediately in MoveNext() instead of on first access.
    /// Recommended when accessing most/all columns to reduce overhead.
    /// </summary>
    public bool EagerParsing { get; init; } = false;

    /// <summary>
    /// Batch size for row boundary caching. Default: 32.
    /// Scans this many row boundaries at once to amortize SIMD overhead.
    /// Set to 0 to disable batching (process one row at a time).
    /// Higher values reduce per-row overhead but use slightly more memory.
    /// </summary>
    public int BatchSize { get; init; } = 32;

    /// <summary>
    /// Default options: comma delimiter, double quote, 10,000 columns, 10,000,000 rows, lazy parsing, batch size 32.
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
