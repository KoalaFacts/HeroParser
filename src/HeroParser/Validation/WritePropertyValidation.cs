namespace HeroParser.Validation;

/// <summary>
/// Holds per-property validation rules for write-side enforcement.
/// </summary>
public sealed record WritePropertyValidation(
    bool NotNull,
    bool NotEmpty,
    int? MaxLength,
    int? MinLength,
    double? RangeMin,
    double? RangeMax,
    string? Pattern,
    int PatternTimeoutMs = 1000)
{
    /// <summary>Gets whether any validation rule is configured.</summary>
    public bool HasAnyRule => NotNull || NotEmpty
        || MaxLength.HasValue || MinLength.HasValue
        || RangeMin.HasValue || RangeMax.HasValue
        || Pattern is not null;
}
