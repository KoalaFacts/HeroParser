namespace HeroParser.SeparatedValues.Records;

/// <summary>
/// Configures how CSV rows are mapped to strongly typed records.
/// </summary>
public sealed record CsvRecordOptions
{
    /// <summary>
    /// Gets or sets a value that indicates whether the CSV includes a header row.
    /// When <see langword="true"/>, the first row is used to resolve column names.
    /// </summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether header name lookup is case-sensitive.
    /// </summary>
    public bool CaseSensitiveHeaders { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether missing columns are tolerated.
    /// When <see langword="false"/>, missing mappings throw <see cref="CsvException"/>.
    /// </summary>
    public bool AllowMissingColumns { get; init; } = false;

    internal StringComparer HeaderComparer => CaseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Gets a reusable default instance.
    /// </summary>
    public static CsvRecordOptions Default { get; } = new();
}
