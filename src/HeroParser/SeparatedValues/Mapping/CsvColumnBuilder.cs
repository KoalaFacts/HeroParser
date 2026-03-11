using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace HeroParser.SeparatedValues.Mapping;

/// <summary>
/// Fluent builder for configuring a single CSV column mapping.
/// </summary>
public sealed class CsvColumnBuilder
{
    /// <summary>Gets the configured header name, or null if not set.</summary>
    public string? HeaderName { get; private set; }

    /// <summary>Gets the configured column index, or null if not set.</summary>
    public int? ColumnIndex { get; private set; }

    /// <summary>Gets the configured format string, or null if not set.</summary>
    public string? FormatString { get; private set; }

    /// <summary>Gets whether the column is required.</summary>
    public bool IsRequired { get; private set; }

    // Validation state
    private bool notEmpty;
    private int? maxLength;
    private int? minLength;
    private double? rangeMin;
    private double? rangeMax;
    private string? pattern;
    private int patternTimeoutMs = 1000;

    /// <summary>Sets the CSV column header name for header-based binding.</summary>
    public CsvColumnBuilder Name(string name) { HeaderName = name; return this; }

    /// <summary>Sets the CSV column index (0-based) for positional binding.</summary>
    public CsvColumnBuilder Index(int index) { ColumnIndex = index; return this; }

    /// <summary>Sets the format string for parsing and writing (e.g., "yyyy-MM-dd", "F2").</summary>
    public CsvColumnBuilder Format(string format) { FormatString = format; return this; }

    /// <summary>Marks the column as required (non-null value).</summary>
    public CsvColumnBuilder Required() { IsRequired = true; return this; }

    /// <summary>Requires the string value to be non-empty and non-whitespace. Most useful for string fields — for numeric fields, the setter parses the value before validation runs.</summary>
    public CsvColumnBuilder NotEmpty() { notEmpty = true; return this; }

    /// <summary>Sets the maximum string length.</summary>
    public CsvColumnBuilder MaxLength(int length) { maxLength = length; return this; }

    /// <summary>Sets the minimum string length.</summary>
    public CsvColumnBuilder MinLength(int length) { minLength = length; return this; }

    /// <summary>Sets the valid numeric range (inclusive).</summary>
    public CsvColumnBuilder Range(double min, double max) { rangeMin = min; rangeMax = max; return this; }

    /// <summary>Sets a regex pattern the string value must match.</summary>
    public CsvColumnBuilder Pattern([StringSyntax(StringSyntaxAttribute.Regex)] string regex, int timeoutMs = 1000)
    {
        pattern = regex;
        patternTimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// Builds a <see cref="CsvPropertyValidation"/> from the configured rules, or null if none set.
    /// </summary>
    internal CsvPropertyValidation? BuildValidation()
    {
        if (!notEmpty && !maxLength.HasValue && !minLength.HasValue &&
            !rangeMin.HasValue && !rangeMax.HasValue && pattern is null)
            return null;

        return new CsvPropertyValidation
        {
            NotEmpty = notEmpty,
            MaxLength = maxLength,
            MinLength = minLength,
            RangeMin = rangeMin,
            RangeMax = rangeMax,
            Pattern = pattern is not null
                ? new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(patternTimeoutMs))
                : null
        };
    }
}
