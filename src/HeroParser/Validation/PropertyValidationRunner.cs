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
        List<ValidationError> errors)
    {
        int initialCount = errors.Count;

        if (notEmpty && string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = columnName,
                RawValue = value,
                Rule = "NotEmpty",
                Message = $"Field '{propertyName}' must not be empty or whitespace.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
        }

        if (minLength.HasValue && value.Length < minLength.Value)
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = columnName,
                RawValue = value,
                Rule = "MinLength",
                Message = $"Field '{propertyName}' length {value.Length} is less than minimum {minLength.Value}.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
        }

        if (maxLength.HasValue && value.Length > maxLength.Value)
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = columnName,
                RawValue = value,
                Rule = "MaxLength",
                Message = $"Field '{propertyName}' length {value.Length} exceeds maximum {maxLength.Value}.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
        }

        if (rangeMin.HasValue || rangeMax.HasValue)
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
            {
                if (rangeMin.HasValue && numericValue < rangeMin.Value)
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = propertyName,
                        ColumnName = columnName,
                        RawValue = value,
                        Rule = "Range",
                        Message = $"Field '{propertyName}' value {numericValue} is less than minimum {rangeMin.Value}.",
                        RowNumber = rowNumber,
                        ColumnIndex = columnIndex
                    });
                }
                if (rangeMax.HasValue && numericValue > rangeMax.Value)
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = propertyName,
                        ColumnName = columnName,
                        RawValue = value,
                        Rule = "Range",
                        Message = $"Field '{propertyName}' value {numericValue} exceeds maximum {rangeMax.Value}.",
                        RowNumber = rowNumber,
                        ColumnIndex = columnIndex
                    });
                }
            }
        }

        if (pattern is { } regex && !regex.IsMatch(value))
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = columnName,
                RawValue = value,
                Rule = "Pattern",
                Message = $"Field '{propertyName}' value '{value}' does not match pattern '{regex}'.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
        }

        return errors.Count > initialCount;
    }
}
