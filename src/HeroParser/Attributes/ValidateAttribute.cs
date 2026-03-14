namespace HeroParser;

/// <summary>
/// Specifies validation constraints that are enforced on both read and write.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ValidateAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c>, the value must not be null or empty. Checked on both read and write.
    /// </summary>
    public bool NotNull { get; init; }

    /// <summary>
    /// When <c>true</c>, the string value must contain at least one non-whitespace character.
    /// Only valid on <c>string</c> properties. Checked on both read and write.
    /// </summary>
    public bool NotEmpty { get; init; }

    /// <summary>
    /// Maximum allowed string length. Set to <c>-1</c> (default) to disable.
    /// Only valid on <c>string</c> properties.
    /// </summary>
    public int MaxLength { get; init; } = -1;

    /// <summary>
    /// Minimum allowed string length. Set to <c>-1</c> (default) to disable.
    /// Only valid on <c>string</c> properties.
    /// </summary>
    public int MinLength { get; init; } = -1;

    /// <summary>
    /// Minimum allowed numeric value (inclusive). Set to <see cref="double.NaN"/> (default) to disable.
    /// Only valid on numeric properties.
    /// </summary>
    public double RangeMin { get; init; } = double.NaN;

    /// <summary>
    /// Maximum allowed numeric value (inclusive). Set to <see cref="double.NaN"/> (default) to disable.
    /// Only valid on numeric properties.
    /// </summary>
    public double RangeMax { get; init; } = double.NaN;

    /// <summary>
    /// Regular expression pattern the string value must match.
    /// Only valid on <c>string</c> properties.
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Regex timeout in milliseconds for <see cref="Pattern"/> validation. Default is 1000ms.
    /// </summary>
    public int PatternTimeoutMs { get; init; } = 1000;
}
