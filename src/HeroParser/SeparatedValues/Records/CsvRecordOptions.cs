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

    /// <summary>
    /// Gets or sets a list of string values that should be treated as null during parsing.
    /// </summary>
    /// <remarks>
    /// When a field value matches one of these strings (case-sensitive), it will be treated as null.
    /// Common examples include "NULL", "N/A", "NA", "null", empty string, etc.
    /// By default, this is null, meaning no special null value handling is performed.
    /// </remarks>
    public IReadOnlyList<string>? NullValues { get; init; } = null;

    internal StringComparer HeaderComparer => CaseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Gets a reusable default instance.
    /// </summary>
    public static CsvRecordOptions Default { get; } = new();
}
