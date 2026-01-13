using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Configures validation rules for CSV data.
/// </summary>
/// <remarks>
/// Thread-Safety: This is an immutable record type and is safe to share across threads.
/// </remarks>
public sealed record CsvValidationOptions
{
    /// <summary>
    /// Gets or sets the delimiter character to use. If null, delimiter will be auto-detected.
    /// </summary>
    public char? Delimiter { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the CSV has a header row (default is true).
    /// </summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>
    /// Gets or sets the list of required header names. Validation fails if any are missing.
    /// </summary>
    public IReadOnlyList<string>? RequiredHeaders { get; init; }

    /// <summary>
    /// Gets or sets the expected column count. If specified, all rows must have this many columns.
    /// </summary>
    public int? ExpectedColumnCount { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of rows allowed (default is 1,000,000).
    /// </summary>
    public int MaxRows { get; init; } = 1_000_000;

    /// <summary>
    /// Gets or sets a value indicating whether to check for consistent column counts across all rows (default is true).
    /// </summary>
    public bool CheckConsistentColumnCount { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether empty files should be considered valid (default is false).
    /// </summary>
    public bool AllowEmptyFile { get; init; } = false;

    /// <summary>
    /// Gets or sets the CSV read options to use for parsing during validation.
    /// If not specified, default options will be used.
    /// </summary>
    public CsvReadOptions? ParseOptions { get; init; }

    /// <summary>
    /// Gets the effective parse options, using ParseOptions if specified or default values.
    /// </summary>
    internal CsvReadOptions GetEffectiveParseOptions()
    {
        if (ParseOptions is not null)
            return ParseOptions;

        return new CsvReadOptions
        {
            Delimiter = Delimiter ?? ',',
            // Don't set MaxRowCount - let the validator handle row limits
            // so it can report TooManyRows instead of ParseError
            EnableQuotedFields = true,
            AllowNewlinesInsideQuotes = true
        };
    }
}
