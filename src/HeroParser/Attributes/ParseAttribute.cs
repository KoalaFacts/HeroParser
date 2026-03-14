namespace HeroParser;

/// <summary>
/// Specifies the format string for read-side type conversion.
/// Also serves as the default write format unless <see cref="FormatAttribute"/> overrides it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ParseAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the format string for parsing (e.g., "yyyy-MM-dd" for dates, "N2" for numbers).
    /// </summary>
    public string? Format { get; init; }
}
