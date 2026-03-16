using System.Text.RegularExpressions;

namespace HeroParser.Validation;

/// <summary>
/// Validates materialized property values against write-side validation rules.
/// </summary>
internal static class WriteValidationRunner
{
    /// <summary>
    /// Validates a materialized property value against the given rules and appends any errors.
    /// Returns <see langword="true"/> if any validation errors were added.
    /// </summary>
    public static bool Validate(
        object? value,
        string propertyName,
        int rowNumber,
        int columnIndex,
        WritePropertyValidation rules,
        List<ValidationError> errors)
    {
        bool hasErrors = false;

        string? rawValue = value?.ToString();

        if (rules.NotNull && (value is null || string.IsNullOrWhiteSpace(rawValue)))
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = null,
                RawValue = rawValue,
                Rule = "NotNull",
                Message = $"Field '{propertyName}' must not be null or empty.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
            hasErrors = true;
        }

        if (rules.NotEmpty && value is not null && string.IsNullOrWhiteSpace(rawValue))
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = null,
                RawValue = rawValue,
                Rule = "NotEmpty",
                Message = $"Field '{propertyName}' must not be empty or whitespace.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
            hasErrors = true;
        }

        if (value is not null)
        {
            string str = rawValue ?? string.Empty;

            if (rules.MinLength.HasValue && str.Length < rules.MinLength.Value)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    ColumnName = null,
                    RawValue = rawValue,
                    Rule = "MinLength",
                    Message = $"Field '{propertyName}' length {str.Length} is less than minimum {rules.MinLength.Value}.",
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex
                });
                hasErrors = true;
            }

            if (rules.MaxLength.HasValue && str.Length > rules.MaxLength.Value)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    ColumnName = null,
                    RawValue = rawValue,
                    Rule = "MaxLength",
                    Message = $"Field '{propertyName}' length {str.Length} exceeds maximum {rules.MaxLength.Value}.",
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex
                });
                hasErrors = true;
            }

            if (rules.RangeMin.HasValue || rules.RangeMax.HasValue)
            {
                double numericValue;
                bool converted;
                try
                {
                    numericValue = Convert.ToDouble(value);
                    converted = true;
                }
                catch (Exception)
                {
                    converted = false;
                    numericValue = 0;
                }

                if (converted)
                {
                    if (rules.RangeMin.HasValue && numericValue < rules.RangeMin.Value)
                    {
                        errors.Add(new ValidationError
                        {
                            PropertyName = propertyName,
                            ColumnName = null,
                            RawValue = rawValue,
                            Rule = "Range",
                            Message = $"Field '{propertyName}' value {numericValue} is less than minimum {rules.RangeMin.Value}.",
                            RowNumber = rowNumber,
                            ColumnIndex = columnIndex
                        });
                        hasErrors = true;
                    }

                    if (rules.RangeMax.HasValue && numericValue > rules.RangeMax.Value)
                    {
                        errors.Add(new ValidationError
                        {
                            PropertyName = propertyName,
                            ColumnName = null,
                            RawValue = rawValue,
                            Rule = "Range",
                            Message = $"Field '{propertyName}' value {numericValue} exceeds maximum {rules.RangeMax.Value}.",
                            RowNumber = rowNumber,
                            ColumnIndex = columnIndex
                        });
                        hasErrors = true;
                    }
                }
            }

            if (rules.Pattern is not null)
            {
                var regex = new Regex(
                    rules.Pattern,
                    RegexOptions.None,
                    TimeSpan.FromMilliseconds(rules.PatternTimeoutMs));

                if (!regex.IsMatch(str))
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = propertyName,
                        ColumnName = null,
                        RawValue = rawValue,
                        Rule = "Pattern",
                        Message = $"Field '{propertyName}' value '{str}' does not match pattern '{rules.Pattern}'.",
                        RowNumber = rowNumber,
                        ColumnIndex = columnIndex
                    });
                    hasErrors = true;
                }
            }
        }

        return hasErrors;
    }
}
