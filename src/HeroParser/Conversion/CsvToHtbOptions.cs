using HeroParser.Htbs;

namespace HeroParser.Conversion;

/// <summary>
/// Configuration options for CSV-to-HTB binary tabular conversion.
/// </summary>
public sealed record CsvToHtbOptions
{
    /// <summary>
    /// Gets or sets the CSV delimiter character (default: comma).
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Gets or sets a value indicating whether the input CSV has a header row (default: true).
    /// </summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>
    /// Gets or sets the maximum number of records allowed to be converted.
    /// </summary>
    public int? MaxRowCount { get; init; }

    /// <summary>
    /// Gets or sets an optional progress reporter.
    /// </summary>
    public IProgress<HtbWriteProgress>? Progress { get; init; }

    /// <summary>
    /// Gets or sets the row interval between progress callbacks (default 1000).
    /// </summary>
    public int ProgressIntervalRows { get; init; } = 1000;

    /// <summary>
    /// Gets or sets a value indicating whether newline characters inside quoted fields are allowed (default: false).
    /// </summary>
    public bool AllowNewlinesInsideQuotes { get; init; } = false;

    /// <summary>
    /// Gets or sets the CSV quote character (default: double quote).
    /// </summary>
    public char Quote { get; init; } = '"';

    /// <summary>
    /// Gets or sets the CSV escape character (default: null).
    /// </summary>
    public char? EscapeCharacter { get; init; }

    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static CsvToHtbOptions Default { get; } = new();
}
