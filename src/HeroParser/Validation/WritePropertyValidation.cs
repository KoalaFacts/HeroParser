using System.Globalization;
using System.Text.RegularExpressions;

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

        if (value is float[] floats)
            return ValidateCollectionLength(floats.Length, propertyName, rowNumber, columnIndex, errors);

        if (value is byte[])
            return false;

        string? rawValue = value as string;
        bool rawValueComputed = value is null or string;

        string? GetRawValue()
        {
            if (!rawValueComputed)
            {
                rawValue = value is IFormattable formattable
                    ? formattable.ToString(null, culture)
                    : value?.ToString();
                rawValueComputed = true;
            }
            return rawValue;
        }

        if (NotNull && (value is null || (!IsKnownNonEmptyValueType(value) && string.IsNullOrWhiteSpace(GetRawValue()))))
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = null,
                RawValue = GetRawValue(),
                Rule = "NotNull",
                Message = $"Field '{propertyName}' must not be null or empty.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
            hasErrors = true;
        }

        if (NotEmpty && value is not null && !IsKnownNonEmptyValueType(value) && string.IsNullOrWhiteSpace(GetRawValue()))
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = null,
                RawValue = GetRawValue(),
                Rule = "NotEmpty",
                Message = $"Field '{propertyName}' must not be empty or whitespace.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
            hasErrors = true;
        }

        if (value is not null)
        {
            if (MinLength.HasValue || MaxLength.HasValue || CompiledPattern is not null)
            {
                string str = GetRawValue() ?? string.Empty;

                if (MinLength.HasValue && str.Length < MinLength.Value)
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = propertyName,
                        ColumnName = null,
                        RawValue = str,
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
                        RawValue = str,
                        Rule = "MaxLength",
                        Message = string.Create(CultureInfo.InvariantCulture, $"Field '{propertyName}' length {str.Length} exceeds maximum {MaxLength.Value}."),
                        RowNumber = rowNumber,
                        ColumnIndex = columnIndex
                    });
                    hasErrors = true;
                }

                if (CompiledPattern is { } regex && !regex.IsMatch(str))
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = propertyName,
                        ColumnName = null,
                        RawValue = str,
                        Rule = "Pattern",
                        Message = $"Field '{propertyName}' value '{str}' does not match pattern '{Pattern}'.",
                        RowNumber = rowNumber,
                        ColumnIndex = columnIndex
                    });
                    hasErrors = true;
                }
            }

            if (RangeMin.HasValue || RangeMax.HasValue)
            {
                if (new NumericRangeCheck(RangeMin, RangeMax).TryEvaluate(value, culture, out bool belowMin, out bool aboveMax, out string formattedValue))
                {
                    if (belowMin)
                    {
                        errors.Add(new ValidationError
                        {
                            PropertyName = propertyName,
                            ColumnName = null,
                            RawValue = formattedValue.Length > 0 ? formattedValue : GetRawValue(),
                            Rule = "Range",
                            Message = string.Create(CultureInfo.InvariantCulture, $"Field '{propertyName}' value {formattedValue} is less than minimum {RangeMin!.Value}."),
                            RowNumber = rowNumber,
                            ColumnIndex = columnIndex
                        });
                        hasErrors = true;
                    }

                    if (aboveMax)
                    {
                        errors.Add(new ValidationError
                        {
                            PropertyName = propertyName,
                            ColumnName = null,
                            RawValue = formattedValue.Length > 0 ? formattedValue : GetRawValue(),
                            Rule = "Range",
                            Message = string.Create(CultureInfo.InvariantCulture, $"Field '{propertyName}' value {formattedValue} exceeds maximum {RangeMax!.Value}."),
                            RowNumber = rowNumber,
                            ColumnIndex = columnIndex
                        });
                        hasErrors = true;
                    }
                }
            }
        }

        return hasErrors;
    }

    internal bool ValidateCollectionLength(
        int length,
        string propertyName,
        int rowNumber,
        int columnIndex,
        List<ValidationError> errors)
    {
        bool hasErrors = false;

        if (MinLength.HasValue && length < MinLength.Value)
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = null,
                RawValue = null,
                Rule = "MinLength",
                Message = string.Create(CultureInfo.InvariantCulture, $"Field '{propertyName}' length {length} is less than minimum {MinLength.Value}."),
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
            hasErrors = true;
        }

        if (MaxLength.HasValue && length > MaxLength.Value)
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = null,
                RawValue = null,
                Rule = "MaxLength",
                Message = string.Create(CultureInfo.InvariantCulture, $"Field '{propertyName}' length {length} exceeds maximum {MaxLength.Value}."),
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
            hasErrors = true;
        }

        return hasErrors;
    }

    private static bool IsKnownNonEmptyValueType(object value)
        => value is bool or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or Half or Int128 or UInt128
            or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan or Guid;
}
