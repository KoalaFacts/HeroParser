using System.Text.RegularExpressions;

namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Validates that a string field matches a regular expression pattern.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class CsvRegexAttribute : CsvValidationAttribute
{
    /// <summary>
    /// Gets the regular expression pattern.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Gets or sets the regex options.
    /// </summary>
    public RegexOptions Options { get; set; } = RegexOptions.None;

    /// <summary>
    /// Gets or sets the timeout for regex matching.
    /// </summary>
    public int MatchTimeoutMilliseconds { get; set; } = 1000;

    private Regex? compiledRegex;

    /// <summary>
    /// Creates a new regex validator with the specified pattern.
    /// </summary>
    /// <param name="pattern">The regular expression pattern.</param>
    public CsvRegexAttribute(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            throw new ArgumentException("Pattern cannot be null or empty.", nameof(pattern));

        Pattern = pattern;
    }

    /// <inheritdoc/>
    public override bool IsValid(object? value, string? rawValue)
    {
        // Null values are valid (use [CsvRequired] for null validation)
        if (value is null)
        {
            return true;
        }

        var stringValue = value.ToString();
        if (stringValue is null)
        {
            return true;
        }

        try
        {
            // Lazy compilation of regex
            compiledRegex ??= new Regex(
                Pattern,
                Options,
                TimeSpan.FromMilliseconds(MatchTimeoutMilliseconds));

            return compiledRegex.IsMatch(stringValue);
        }
        catch (RegexMatchTimeoutException)
        {
            // Timeout means validation failed
            return false;
        }
    }

    /// <inheritdoc/>
    protected override string GetDefaultErrorMessage(string fieldName, object? value)
    {
        return $"'{fieldName}' does not match the required pattern.";
    }
}
