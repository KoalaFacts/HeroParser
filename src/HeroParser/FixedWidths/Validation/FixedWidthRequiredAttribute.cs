namespace HeroParser.FixedWidths.Validation;

/// <summary>
/// Validates that a fixed-width field is not null, empty, or whitespace.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FixedWidthRequiredAttribute : FixedWidthValidationAttribute
{
    /// <summary>
    /// Gets or sets whether empty strings are allowed.
    /// </summary>
    /// <remarks>
    /// When false (default), empty strings fail validation.
    /// When true, only null values fail validation.
    /// </remarks>
    public bool AllowEmptyStrings { get; set; } = false;

    /// <summary>
    /// Gets or sets whether whitespace-only strings are allowed.
    /// </summary>
    /// <remarks>
    /// When false (default), whitespace-only strings fail validation.
    /// When true, strings with only whitespace pass validation (unless <see cref="AllowEmptyStrings"/> is also false).
    /// </remarks>
    public bool AllowWhitespace { get; set; } = false;

    /// <inheritdoc/>
    public override bool IsValid(object? value, string? rawValue)
    {
        if (value is null)
        {
            return false;
        }

        if (value is string stringValue)
        {
            if (!AllowEmptyStrings && string.IsNullOrEmpty(stringValue))
            {
                return false;
            }

            if (!AllowWhitespace && string.IsNullOrWhiteSpace(stringValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    protected override string GetDefaultErrorMessage(string fieldName, object? value)
    {
        return $"'{fieldName}' is required.";
    }
}
