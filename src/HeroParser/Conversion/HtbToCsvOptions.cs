using HeroParser.Htbs;

namespace HeroParser.Conversion;

/// <summary>
/// Configuration options for HTB-to-CSV text tabular conversion.
/// </summary>
public sealed record HtbToCsvOptions
{
    /// <summary>
    /// Gets or sets the CSV delimiter character (default: comma).
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Gets or sets a value indicating whether to write a CSV header row (default: true).
    /// </summary>
    public bool IncludeHeaderRow { get; init; } = true;

    /// <summary>
    /// Gets or sets the newline sequence to use for output (default: CRLF).
    /// </summary>
    public string NewLine { get; init; } = "\r\n";

    /// <summary>
    /// Gets or sets a value indicating whether to wrap all CSV fields in double quotes (default: false).
    /// </summary>
    public bool QuoteAll { get; init; } = false;

    /// <summary>
    /// Gets or sets an optional progress reporter.
    /// </summary>
    public IProgress<HtbProgress>? Progress { get; init; }

    /// <summary>
    /// Gets or sets the row interval between progress callbacks (default 1000).
    /// </summary>
    public int ProgressIntervalRows { get; init; } = 1000;

    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static HtbToCsvOptions Default { get; } = new();
}
