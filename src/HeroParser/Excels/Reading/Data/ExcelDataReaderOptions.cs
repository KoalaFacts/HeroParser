namespace HeroParser.Excels.Reading.Data;

/// <summary>
/// Configures how <see cref="ExcelDataReader"/> exposes Excel worksheet data via <see cref="System.Data.IDataReader"/>.
/// </summary>
public sealed record ExcelDataReaderOptions
{
    /// <summary>
    /// Gets or sets a value that indicates whether the Excel data includes a header row.
    /// When <see langword="true"/>, the first data row (after any skipped rows) is used for column names.
    /// </summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether header name lookup is case-sensitive.
    /// </summary>
    public bool CaseSensitiveHeaders { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether rows with fewer columns than the header are tolerated.
    /// When <see langword="false"/>, accessing a missing column returns <see cref="DBNull.Value"/>.
    /// </summary>
    public bool AllowMissingColumns { get; init; } = false;

    /// <summary>
    /// Gets or sets a list of string values that should be treated as null during reading.
    /// Cells whose string value matches any entry in this list are exposed as <see cref="DBNull.Value"/>.
    /// </summary>
    public IReadOnlyList<string>? NullValues { get; init; } = null;

    /// <summary>
    /// Gets or sets explicit column names to use instead of header row values.
    /// </summary>
    /// <remarks>
    /// When provided, these names override any values read from the header row.
    /// If <see cref="HasHeaderRow"/> is <see langword="false"/>, these names define the schema directly.
    /// </remarks>
    public IReadOnlyList<string>? ColumnNames { get; init; } = null;

    /// <summary>
    /// Gets or sets the number of rows to skip before reading the header or data rows.
    /// </summary>
    public int SkipRows { get; init; } = 0;

    internal StringComparer HeaderComparer => CaseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Gets a reusable default instance.
    /// </summary>
    public static ExcelDataReaderOptions Default { get; } = new();
}
