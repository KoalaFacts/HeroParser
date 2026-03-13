namespace HeroParser.Excel.Core;

/// <summary>
/// Options for configuring Excel .xlsx reading behavior.
/// </summary>
public sealed record ExcelReadOptions
{
    /// <summary>Whether the first row contains column headers. Default: true.</summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>Culture for parsing cell values. Default: null (InvariantCulture).</summary>
    public System.Globalization.CultureInfo? Culture { get; init; }

    /// <summary>Maximum number of rows to read (excluding header). Default: null (no limit).</summary>
    public int? MaxRowCount { get; init; }

    /// <summary>Progress callback for reporting reading progress.</summary>
    public IProgress<ExcelProgress>? Progress { get; init; }
}
