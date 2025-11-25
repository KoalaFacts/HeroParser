using System.Globalization;

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
    /// Gets or sets the culture to use when parsing culture-sensitive values like dates and numbers.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> (the default), <see cref="CultureInfo.InvariantCulture"/> is used.
    /// This affects parsing of numeric types, dates, and other culture-sensitive types.
    /// </remarks>
    public CultureInfo? Culture { get; init; } = null;

    /// <summary>
    /// Gets or sets the number of rows to skip from the start of the CSV data.
    /// </summary>
    /// <remarks>
    /// Use this to skip metadata rows or other non-data content at the beginning of the file.
    /// The header row (if <see cref="HasHeaderRow"/> is true) is expected after the skipped rows.
    /// </remarks>
    public int SkipRows { get; init; } = 0;

    internal StringComparer HeaderComparer => CaseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Gets a reusable default instance.
    /// </summary>
    public static CsvRecordOptions Default { get; } = new();
}
