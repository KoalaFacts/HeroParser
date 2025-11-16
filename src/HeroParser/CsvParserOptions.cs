namespace HeroParser;

/// <summary>
/// Options for configuring CSV parser behavior.
/// </summary>
public sealed class CsvParserOptions
{
    /// <summary>
    /// Field delimiter character. Default: comma (',').
    /// Must be ASCII (0-127) for SIMD performance.
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Maximum columns per row. Default: 10,000.
    /// Throws CsvException if exceeded.
    /// </summary>
    public int MaxColumns { get; init; } = 10_000;

    /// <summary>
    /// Maximum rows to parse. Default: 100,000.
    /// Throws CsvException if exceeded.
    /// </summary>
    public int MaxRows { get; init; } = 100_000;

    /// <summary>
    /// Default options: comma delimiter, 10,000 columns, 100,000 rows.
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
