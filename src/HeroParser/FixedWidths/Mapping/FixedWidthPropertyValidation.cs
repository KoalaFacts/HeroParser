using System.Text.RegularExpressions;

namespace HeroParser.FixedWidths.Mapping;

/// <summary>
/// Runtime validation rules for a fixed-width property, used by descriptor-based binding.
/// </summary>
public sealed class FixedWidthPropertyValidation
{
    /// <summary>Gets whether the field must be non-null.</summary>
    public bool NotEmpty { get; init; }

    /// <summary>Gets the maximum string length, or null if unconstrained.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Gets the minimum string length, or null if unconstrained.</summary>
    public int? MinLength { get; init; }

    /// <summary>Gets the minimum range value, or null if unconstrained.</summary>
    public double? RangeMin { get; init; }

    /// <summary>Gets the maximum range value, or null if unconstrained.</summary>
    public double? RangeMax { get; init; }

    /// <summary>Gets the regex pattern, or null if unconstrained.</summary>
    public Regex? Pattern { get; init; }

    /// <summary>Gets whether any validation rule is configured.</summary>
    internal bool HasAnyRule =>
        NotEmpty || MaxLength.HasValue || MinLength.HasValue ||
        RangeMin.HasValue || RangeMax.HasValue || Pattern is not null;
}
