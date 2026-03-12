using System.Globalization;
using System.Text.RegularExpressions;

namespace HeroParser.Validation;

/// <summary>
/// Shared validation runner that checks field values against configured rules.
/// Used by both CSV and FixedWidth descriptor binders.
/// </summary>
internal static class PropertyValidationRunner
{
    /// <summary>
    /// Validates a field value against the given rules and appends any errors.
    /// Returns true if any validation errors were added (caller should exclude the row).
    /// </summary>
    public static bool Validate(
        string value,
        string propertyName,
        int rowNumber,
        int columnIndex,
        string? columnName,
        bool notEmpty,
        int? minLength,
        int? maxLength,
        double? rangeMin,
        double? rangeMax,
        Regex? pattern,
        List<ValidationError>? errors,
        CultureInfo culture)
    {
        bool hasErrors = false;

        void AddError(string rule, string message)
        {
            hasErrors = true;
            errors?.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = columnName,
                RawValue = value,
                Rule = rule,
                Message = message,
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
        }

        if (notEmpty && string.IsNullOrWhiteSpace(value))
        {
            AddError("NotEmpty", $"Field '{propertyName}' must not be empty or whitespace.");
        }

        if (minLength.HasValue && value.Length < minLength.Value)
        {
            AddError("MinLength", $"Field '{propertyName}' length {value.Length} is less than minimum {minLength.Value}.");
        }

        if (maxLength.HasValue && value.Length > maxLength.Value)
        {
            AddError("MaxLength", $"Field '{propertyName}' length {value.Length} exceeds maximum {maxLength.Value}.");
        }

        if (rangeMin.HasValue || rangeMax.HasValue)
        {
            if (double.TryParse(value, NumberStyles.Any, culture, out var numericValue))
            {
                if (rangeMin.HasValue && numericValue < rangeMin.Value)
                {
                    AddError("Range", $"Field '{propertyName}' value {numericValue} is less than minimum {rangeMin.Value}.");
                }
                if (rangeMax.HasValue && numericValue > rangeMax.Value)
                {
                    AddError("Range", $"Field '{propertyName}' value {numericValue} exceeds maximum {rangeMax.Value}.");
                }
            }
        }

        if (pattern is { } regex && !regex.IsMatch(value))
        {
            AddError("Pattern", $"Field '{propertyName}' value '{value}' does not match pattern '{regex}'.");
        }

        return hasErrors;
    }
}
