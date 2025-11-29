namespace HeroParser.FixedWidths;

/// <summary>
/// Configuration options for parsing fixed-width files.
/// </summary>
public sealed record FixedWidthParserOptions
{
    /// <summary>
    /// Gets or sets the fixed record length in characters.
    /// When <see langword="null"/>, each line (terminated by newline) is treated as one record.
    /// When set, records are read as fixed-length blocks without regard to line endings.
    /// </summary>
    public int? RecordLength { get; init; }

    /// <summary>
    /// Gets or sets the default padding character for all fields.
    /// Individual fields can override this via <see cref="Records.Binding.FixedWidthColumnAttribute.PadChar"/>.
    /// </summary>
    public char DefaultPadChar { get; init; } = ' ';

    /// <summary>
    /// Gets or sets the default field alignment for all fields.
    /// Individual fields can override this via <see cref="Records.Binding.FixedWidthColumnAttribute.Alignment"/>.
    /// </summary>
    public FieldAlignment DefaultAlignment { get; init; } = FieldAlignment.Left;

    /// <summary>
    /// Gets or sets the maximum number of records to parse.
    /// Parsing stops when this limit is reached.
    /// </summary>
    public int MaxRecordCount { get; init; } = 100_000;

    /// <summary>
    /// Gets or sets whether to track source line numbers for error reporting.
    /// When <see langword="true"/>, the <see cref="FixedWidthCharSpanRow.SourceLineNumber"/> property is populated.
    /// </summary>
    public bool TrackSourceLineNumbers { get; init; } = false;

    /// <summary>
    /// Gets or sets whether to skip empty lines when using line-based parsing.
    /// Only applies when <see cref="RecordLength"/> is <see langword="null"/>.
    /// </summary>
    public bool SkipEmptyLines { get; init; } = true;

    /// <summary>
    /// Gets or sets the comment character. Lines starting with this character are skipped.
    /// Only applies when <see cref="RecordLength"/> is <see langword="null"/>.
    /// </summary>
    public char? CommentCharacter { get; init; }

    /// <summary>
    /// Gets or sets the number of rows to skip before parsing data.
    /// Useful for skipping header rows or metadata at the start of the file.
    /// </summary>
    public int SkipRows { get; init; } = 0;

    /// <summary>
    /// Gets or sets the maximum input size in bytes for file and stream operations.
    /// When <see langword="null"/>, no limit is enforced.
    /// Default is 100 MB to prevent accidental memory exhaustion.
    /// </summary>
    public long? MaxInputSize { get; init; } = 100 * 1024 * 1024; // 100 MB

    /// <summary>
    /// Gets the default parser options instance.
    /// </summary>
    public static FixedWidthParserOptions Default { get; } = new();

    /// <summary>
    /// Validates the options and throws if invalid.
    /// </summary>
    /// <exception cref="FixedWidthException">Thrown when options are invalid.</exception>
    public void Validate()
    {
        if (RecordLength is not null && RecordLength.Value <= 0)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                $"RecordLength must be positive when specified. Value: {RecordLength.Value}");
        }

        if (MaxRecordCount <= 0)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                $"MaxRecordCount must be positive. Value: {MaxRecordCount}");
        }

        if (SkipRows < 0)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                $"SkipRows must be non-negative. Value: {SkipRows}");
        }

        if (MaxInputSize is not null && MaxInputSize.Value <= 0)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                $"MaxInputSize must be positive when specified. Value: {MaxInputSize.Value}");
        }
    }

    /// <summary>
    /// Validates that the input size does not exceed the configured limit.
    /// </summary>
    /// <param name="size">The input size in bytes.</param>
    /// <exception cref="FixedWidthException">Thrown when the size exceeds <see cref="MaxInputSize"/>.</exception>
    internal void ValidateInputSize(long size)
    {
        if (MaxInputSize is not null && size > MaxInputSize.Value)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                $"Input size ({size:N0} bytes) exceeds maximum allowed ({MaxInputSize.Value:N0} bytes). " +
                $"Set MaxInputSize to a larger value or null to disable this check.");
        }
    }
}
