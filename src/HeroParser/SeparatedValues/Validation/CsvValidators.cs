using System.Text.RegularExpressions;

namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Provides built-in validators for the fluent validation API.
/// </summary>
public static class CsvValidators
{
    /// <summary>
    /// Creates a validator that requires a non-null, non-empty value.
    /// </summary>
    /// <param name="allowEmptyStrings">When true, allows empty strings.</param>
    /// <param name="allowWhitespace">When true, allows whitespace-only strings.</param>
    public static IFieldValidator Required(bool allowEmptyStrings = false, bool allowWhitespace = false)
    {
        return new RequiredValidator(allowEmptyStrings, allowWhitespace);
    }

    /// <summary>
    /// Creates a validator that checks string length bounds.
    /// </summary>
    /// <param name="minLength">The minimum allowed length.</param>
    /// <param name="maxLength">The maximum allowed length.</param>
    public static IFieldValidator StringLength(int minLength, int maxLength)
    {
        return new StringLengthValidator(minLength, maxLength);
    }

    /// <summary>
    /// Creates a validator that checks string length bounds.
    /// </summary>
    /// <param name="maxLength">The maximum allowed length.</param>
    public static IFieldValidator MaxLength(int maxLength)
    {
        return new StringLengthValidator(0, maxLength);
    }

    /// <summary>
    /// Creates a validator that checks numeric range.
    /// </summary>
    /// <typeparam name="T">The numeric type.</typeparam>
    /// <param name="min">The minimum allowed value (inclusive).</param>
    /// <param name="max">The maximum allowed value (inclusive).</param>
    public static IFieldValidator Range<T>(T min, T max) where T : IComparable<T>
    {
        return new RangeValidator<T>(min, max);
    }

    /// <summary>
    /// Creates a validator that matches a regular expression pattern.
    /// </summary>
    /// <param name="pattern">The regex pattern.</param>
    /// <param name="options">The regex options.</param>
    public static IFieldValidator Regex(string pattern, RegexOptions options = RegexOptions.None)
    {
        return new RegexValidator(pattern, options);
    }

    /// <summary>
    /// Creates a validator that checks for CSV injection patterns (dangerous leading characters).
    /// </summary>
    /// <remarks>
    /// <para>Always rejects values that start with: =, @, tab, or carriage return.</para>
    /// <para>
    /// For '-' and '+', uses smart detection: allows if followed by a digit or decimal point
    /// (e.g., -123, +1-555-1234, -.5), but rejects if followed by letters or other characters
    /// that could indicate a formula (e.g., -HYPERLINK(), +SUM()).
    /// </para>
    /// </remarks>
    public static IFieldValidator NoInjection()
    {
        return new NoInjectionValidator();
    }

    /// <summary>
    /// Creates a custom validator using a predicate function.
    /// </summary>
    /// <param name="predicate">The validation predicate.</param>
    /// <param name="errorMessage">The error message when validation fails.</param>
    public static IFieldValidator Custom(Func<object?, bool> predicate, string errorMessage)
    {
        return new CustomValidator(predicate, errorMessage);
    }

    /// <summary>
    /// Creates a custom validator using a string predicate function.
    /// </summary>
    /// <param name="predicate">The validation predicate for string values.</param>
    /// <param name="errorMessage">The error message when validation fails.</param>
    public static IFieldValidator Custom(Func<string?, bool> predicate, string errorMessage)
    {
        return new CustomValidator(v => predicate(v?.ToString()), errorMessage);
    }

    #region Validator Implementations

    private sealed class RequiredValidator : IFieldValidator
    {
        private readonly bool allowEmptyStrings;
        private readonly bool allowWhitespace;

        public RequiredValidator(bool allowEmptyStrings, bool allowWhitespace)
        {
            this.allowEmptyStrings = allowEmptyStrings;
            this.allowWhitespace = allowWhitespace;
        }

        public CsvValidationResult Validate(object? value, string? rawValue)
        {
            if (value is null)
            {
                return CsvValidationResult.Failure("Value is required.");
            }

            if (value is string stringValue)
            {
                if (!allowEmptyStrings && string.IsNullOrEmpty(stringValue))
                {
                    return CsvValidationResult.Failure("Value cannot be empty.");
                }

                if (!allowWhitespace && string.IsNullOrWhiteSpace(stringValue))
                {
                    return CsvValidationResult.Failure("Value cannot be whitespace only.");
                }
            }

            return CsvValidationResult.Success;
        }
    }

    private sealed class StringLengthValidator : IFieldValidator
    {
        private readonly int minLength;
        private readonly int maxLength;

        public StringLengthValidator(int minLength, int maxLength)
        {
            this.minLength = minLength;
            this.maxLength = maxLength;
        }

        public CsvValidationResult Validate(object? value, string? rawValue)
        {
            if (value is null)
            {
                return CsvValidationResult.Success;
            }

            var stringValue = value.ToString();
            if (stringValue is null)
            {
                return CsvValidationResult.Success;
            }

            var length = stringValue.Length;
            if (length < minLength)
            {
                return CsvValidationResult.Failure($"Value must be at least {minLength} characters.");
            }

            if (length > maxLength)
            {
                return CsvValidationResult.Failure($"Value must not exceed {maxLength} characters.");
            }

            return CsvValidationResult.Success;
        }
    }

    private sealed class RangeValidator<T> : IFieldValidator where T : IComparable<T>
    {
        private readonly T min;
        private readonly T max;

        public RangeValidator(T min, T max)
        {
            this.min = min;
            this.max = max;
        }

        public CsvValidationResult Validate(object? value, string? rawValue)
        {
            if (value is null)
            {
                return CsvValidationResult.Success;
            }

            if (value is T typedValue)
            {
                if (typedValue.CompareTo(min) < 0)
                {
                    return CsvValidationResult.Failure($"Value must be at least {min}.");
                }

                if (typedValue.CompareTo(max) > 0)
                {
                    return CsvValidationResult.Failure($"Value must not exceed {max}.");
                }

                return CsvValidationResult.Success;
            }

            // Try conversion for compatible types
            try
            {
                var converted = (IComparable)Convert.ChangeType(value, typeof(T));
                if (converted.CompareTo(min) < 0 || converted.CompareTo(max) > 0)
                {
                    return CsvValidationResult.Failure($"Value must be between {min} and {max}.");
                }
                return CsvValidationResult.Success;
            }
            catch
            {
                return CsvValidationResult.Failure($"Value must be between {min} and {max}.");
            }
        }
    }

    private sealed class RegexValidator : IFieldValidator
    {
        /// <summary>
        /// Regex timeout to prevent ReDoS attacks. 150ms is enough for typical validation
        /// while limiting exposure to catastrophic backtracking patterns.
        /// </summary>
        private static readonly TimeSpan regexTimeout = TimeSpan.FromMilliseconds(150);

        private readonly Regex regex;

        public RegexValidator(string pattern, RegexOptions options)
        {
            regex = new Regex(pattern, options, regexTimeout);
        }

        public CsvValidationResult Validate(object? value, string? rawValue)
        {
            if (value is null)
            {
                return CsvValidationResult.Success;
            }

            var stringValue = value.ToString();
            if (stringValue is null)
            {
                return CsvValidationResult.Success;
            }

            try
            {
                if (!regex.IsMatch(stringValue))
                {
                    return CsvValidationResult.Failure("Value does not match the required pattern.");
                }
                return CsvValidationResult.Success;
            }
            catch (RegexMatchTimeoutException)
            {
                return CsvValidationResult.Failure("Pattern matching timed out.");
            }
        }
    }

    private sealed class NoInjectionValidator : IFieldValidator
    {
        public CsvValidationResult Validate(object? value, string? rawValue)
        {
            if (value is null)
            {
                return CsvValidationResult.Success;
            }

            var stringValue = value.ToString();
            if (string.IsNullOrEmpty(stringValue))
            {
                return CsvValidationResult.Success;
            }

            var firstChar = stringValue[0];

            // Use switch for O(1) lookup of dangerous characters
            switch (firstChar)
            {
                // Always-dangerous characters
                case '=':
                case '@':
                case '\t':
                case '\r':
                    return CsvValidationResult.Failure($"Value starts with a potentially dangerous character '{firstChar}'.");

                // Smart detection for '-' and '+':
                // Safe if followed by digit or '.', dangerous otherwise
                case '-':
                case '+':
                    if (stringValue.Length > 1)
                    {
                        var secondChar = stringValue[1];
                        // Safe patterns: -123, +1-555, -.5, +.5
                        // Use uint comparison to check digit range in one instruction
                        if (!((uint)(secondChar - '0') <= 9 || secondChar == '.'))
                        {
                            // Dangerous patterns: -HYPERLINK(, +SUM(, -A1
                            return CsvValidationResult.Failure($"Value starts with '{firstChar}' followed by a non-numeric character, which could be a formula.");
                        }
                    }
                    return CsvValidationResult.Success;

                default:
                    return CsvValidationResult.Success;
            }
        }
    }

    private sealed class CustomValidator : IFieldValidator
    {
        private readonly Func<object?, bool> predicate;
        private readonly string errorMessage;

        public CustomValidator(Func<object?, bool> predicate, string errorMessage)
        {
            this.predicate = predicate;
            this.errorMessage = errorMessage;
        }

        public CsvValidationResult Validate(object? value, string? rawValue)
        {
            if (predicate(value))
            {
                return CsvValidationResult.Success;
            }

            return CsvValidationResult.Failure(errorMessage);
        }
    }

    #endregion
}
