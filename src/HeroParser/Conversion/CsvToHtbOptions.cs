using HeroParser.Htbs;
using HeroParser.SeparatedValues.Core;

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
    /// Gets or sets a value indicating whether CSV columns map to <see cref="Htbs.Records.HtbSchema"/> columns by positional ordinal instead of header name matching (default: false).
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/> and <see cref="HasHeaderRow"/> is <see langword="true"/>, the first row is consumed as a header
    /// and subsequent rows map positionally (<c>0 -&gt; 0</c>, <c>1 -&gt; 1</c>, ...), preserving columns with duplicate or blank CSV header names.
    /// </remarks>
    public bool MapByOrdinal { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether CSV rows containing more columns than <see cref="Htbs.Records.HtbSchema"/> throw an <see cref="Htbs.HtbException"/> instead of ignoring extra columns (default: false).
    /// </summary>
    public bool RejectExtraColumns { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether numeric CSV values that cannot round-trip identically through the target HTB numeric type (such as leading zeros like <c>00123</c> or values exceeding decimal precision) throw an <see cref="Htbs.HtbException"/> instead of silently normalizing or rounding (default: false).
    /// </summary>
    public bool RejectLossyNumericValues { get; init; } = false;

    /// <summary>
    /// Gets or sets the maximum number of records allowed to be converted.
    /// </summary>
    public int? MaxRowCount { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of CSV rows parsed from the input (including any header row).
    /// When <see langword="null"/>, uses at least <see cref="CsvReadOptions.MaxRowCount"/> (expanded to fit <see cref="MaxRowCount"/> when set).
    /// </summary>
    public int? MaxInputRowCount { get; init; }

    /// <summary>
    /// Gets or sets the maximum CSV input row size in characters or bytes, or <see langword="null"/> to disable the limit.
    /// Defaults to <see cref="CsvReadOptions.MaxRowSize"/>.
    /// </summary>
    public int? MaxInputRowSize { get; init; } = CsvReadOptions.Default.MaxRowSize;

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
