using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using HeroParser.Internal;

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

    // Compiled once per rule instance — avoids per-row Regex construction in the write hot path.
    internal Regex? CompiledPattern { get; } = Pattern is null
        ? null
        : new Regex(Pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(PatternTimeoutMs));

    internal static WritePropertyValidation? FromAttribute(ValidateAttribute? validateAttr, bool requireAnyRule = false)
    {
        if (validateAttr is null)
            return null;

        var validation = new WritePropertyValidation(
            validateAttr.NotNull,
            validateAttr.NotEmpty,
            validateAttr.MaxLength >= 0 ? validateAttr.MaxLength : null,
            validateAttr.MinLength >= 0 ? validateAttr.MinLength : null,
            !double.IsNaN(validateAttr.RangeMin) ? validateAttr.RangeMin : null,
            !double.IsNaN(validateAttr.RangeMax) ? validateAttr.RangeMax : null,
            validateAttr.Pattern,
            validateAttr.PatternTimeoutMs);

        return requireAnyRule && !validation.HasAnyRule ? null : validation;
    }

    /// <summary>
    /// Validates a materialized property value against this instance's rules and appends any errors.
    /// Returns <see langword="true"/> if any validation errors were added.
    /// </summary>
    internal bool Validate(
        object? value,
        string propertyName,
        int rowNumber,
        int columnIndex,
        List<ValidationError> errors,
        CultureInfo culture)
    {
        bool hasErrors = false;

        string? rawValue = value is IFormattable formattable
            ? formattable.ToString(null, culture)
            : value?.ToString();

        if (NotNull && (value is null || string.IsNullOrWhiteSpace(rawValue)))
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

        if (NotEmpty && value is not null && string.IsNullOrWhiteSpace(rawValue))
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

            if (MinLength.HasValue && str.Length < MinLength.Value)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    ColumnName = null,
                    RawValue = rawValue,
                    Rule = "MinLength",
                    Message = string.Create(CultureInfo.InvariantCulture, $"Field '{propertyName}' length {str.Length} is less than minimum {MinLength.Value}."),
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex
                });
                hasErrors = true;
            }

            if (MaxLength.HasValue && str.Length > MaxLength.Value)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    ColumnName = null,
                    RawValue = rawValue,
                    Rule = "MaxLength",
                    Message = string.Create(CultureInfo.InvariantCulture, $"Field '{propertyName}' length {str.Length} exceeds maximum {MaxLength.Value}."),
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex
                });
                hasErrors = true;
            }

            if (RangeMin.HasValue || RangeMax.HasValue)
            {
                if (TryToDouble(value, culture, out double numericValue))
                {
                    if (RangeMin.HasValue && numericValue < RangeMin.Value)
                    {
                        errors.Add(new ValidationError
                        {
                            PropertyName = propertyName,
                            ColumnName = null,
                            RawValue = rawValue,
                            Rule = "Range",
                            Message = string.Create(CultureInfo.InvariantCulture, $"Field '{propertyName}' value {numericValue} is less than minimum {RangeMin.Value}."),
                            RowNumber = rowNumber,
                            ColumnIndex = columnIndex
                        });
                        hasErrors = true;
                    }

                    if (RangeMax.HasValue && numericValue > RangeMax.Value)
                    {
                        errors.Add(new ValidationError
                        {
                            PropertyName = propertyName,
                            ColumnName = null,
                            RawValue = rawValue,
                            Rule = "Range",
                            Message = string.Create(CultureInfo.InvariantCulture, $"Field '{propertyName}' value {numericValue} exceeds maximum {RangeMax.Value}."),
                            RowNumber = rowNumber,
                            ColumnIndex = columnIndex
                        });
                        hasErrors = true;
                    }
                }
            }

            if (CompiledPattern is { } regex)
            {
                if (!regex.IsMatch(str))
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = propertyName,
                        ColumnName = null,
                        RawValue = rawValue,
                        Rule = "Pattern",
                        Message = $"Field '{propertyName}' value '{str}' does not match pattern '{Pattern}'.",
                        RowNumber = rowNumber,
                        ColumnIndex = columnIndex
                    });
                    hasErrors = true;
                }
            }
        }

        return hasErrors;
    }

    private static bool TryToDouble(object value, CultureInfo culture, out double numericValue)
    {
        switch (value)
        {
            case string text:
                // Same styles as Convert.ToDouble(string); also accepts ASCII '-' where the culture's sign carries bidi marks.
                return new CultureNumberParser(culture).TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, out numericValue);
            case Half h:
                numericValue = (double)h;
                return true;
            case Int128 i128:
                numericValue = (double)i128;
                return true;
            case UInt128 u128:
                numericValue = (double)u128;
                return true;
            case BigInteger bi:
                numericValue = (double)bi;
                return true;
            default:
                try
                {
                    numericValue = Convert.ToDouble(value, culture);
                    return true;
                }
                catch (Exception)
                {
                    numericValue = 0;
                    return false;
                }
        }
    }
}
