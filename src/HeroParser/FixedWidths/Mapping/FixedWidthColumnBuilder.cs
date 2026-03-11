using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace HeroParser.FixedWidths.Mapping;

/// <summary>
/// Fluent builder for configuring a single fixed-width column mapping.
/// </summary>
public sealed class FixedWidthColumnBuilder
{
    /// <summary>Gets the configured start position, or null if not set.</summary>
    public int? StartPosition { get; private set; }

    /// <summary>Gets the configured field length, or null if not set.</summary>
    public int? FieldLength { get; private set; }

    /// <summary>Gets the configured padding character, or null for default.</summary>
    public char? FieldPadChar { get; private set; }

    /// <summary>Gets the configured field alignment, or null for default.</summary>
    public FieldAlignment? FieldAlignment { get; private set; }

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

    // End position for computing length
    private int? EndPosition { get; set; }

    /// <summary>Sets the 0-based start position of the field.</summary>
    public FixedWidthColumnBuilder Start(int start) { StartPosition = start; return this; }

    /// <summary>Sets the field length in characters.</summary>
    public FixedWidthColumnBuilder Length(int length) { FieldLength = length; return this; }

    /// <summary>
    /// Sets the exclusive end position. Length is computed as End - Start.
    /// If both Length and End are set, End takes precedence.
    /// </summary>
    public FixedWidthColumnBuilder End(int end) { EndPosition = end; return this; }

    /// <summary>Sets the padding character for this field.</summary>
    public FixedWidthColumnBuilder PadChar(char padChar) { FieldPadChar = padChar; return this; }

    /// <summary>Sets the field alignment for this field.</summary>
    public FixedWidthColumnBuilder Alignment(FieldAlignment alignment) { FieldAlignment = alignment; return this; }

    /// <summary>Sets the format string for parsing and writing (e.g., "yyyy-MM-dd", "F2").</summary>
    public FixedWidthColumnBuilder Format(string format) { FormatString = format; return this; }

    /// <summary>Marks the column as required (non-null value).</summary>
    public FixedWidthColumnBuilder Required() { IsRequired = true; return this; }

    /// <summary>Requires the string value to be non-empty and non-whitespace.</summary>
    public FixedWidthColumnBuilder NotEmpty() { notEmpty = true; return this; }

    /// <summary>Sets the maximum string length.</summary>
    public FixedWidthColumnBuilder MaxLength(int length) { maxLength = length; return this; }

    /// <summary>Sets the minimum string length.</summary>
    public FixedWidthColumnBuilder MinLength(int length) { minLength = length; return this; }

    /// <summary>Sets the valid numeric range (inclusive).</summary>
    public FixedWidthColumnBuilder Range(double min, double max) { rangeMin = min; rangeMax = max; return this; }

    /// <summary>Sets a regex pattern the string value must match.</summary>
    public FixedWidthColumnBuilder Pattern([StringSyntax(StringSyntaxAttribute.Regex)] string regex, int timeoutMs = 1000)
    {
        pattern = regex;
        patternTimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// Resolves the effective field length, applying End-based computation if set.
    /// End takes precedence over Length when both are specified.
    /// </summary>
    internal int? ResolvedFieldLength
    {
        get
        {
            if (EndPosition.HasValue && StartPosition.HasValue)
                return EndPosition.Value - StartPosition.Value;
            return FieldLength;
        }
    }

    /// <summary>
    /// Builds a <see cref="FixedWidthPropertyValidation"/> from the configured rules, or null if none set.
    /// </summary>
    internal FixedWidthPropertyValidation? BuildValidation()
    {
        if (!notEmpty && !maxLength.HasValue && !minLength.HasValue &&
            !rangeMin.HasValue && !rangeMax.HasValue && pattern is null)
            return null;

        return new FixedWidthPropertyValidation
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
