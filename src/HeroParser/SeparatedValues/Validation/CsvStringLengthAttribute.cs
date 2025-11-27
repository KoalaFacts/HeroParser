namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Validates that a string field length is within the specified bounds.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CsvStringLengthAttribute : CsvValidationAttribute
{
    /// <summary>
    /// Gets the minimum allowed length.
    /// </summary>
    public int MinLength { get; }

    /// <summary>
    /// Gets the maximum allowed length.
    /// </summary>
    public int MaxLength { get; }

    /// <summary>
    /// Creates a new string length validator with the specified maximum length.
    /// </summary>
    /// <param name="maxLength">The maximum allowed length.</param>
    public CsvStringLengthAttribute(int maxLength)
        : this(0, maxLength)
    {
    }

    /// <summary>
    /// Creates a new string length validator with the specified minimum and maximum lengths.
    /// </summary>
    /// <param name="minLength">The minimum allowed length.</param>
    /// <param name="maxLength">The maximum allowed length.</param>
    public CsvStringLengthAttribute(int minLength, int maxLength)
    {
        if (minLength < 0)
            throw new ArgumentOutOfRangeException(nameof(minLength), "Minimum length cannot be negative.");
        if (maxLength < minLength)
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Maximum length cannot be less than minimum length.");

        MinLength = minLength;
        MaxLength = maxLength;
    }

    /// <inheritdoc/>
    public override bool IsValid(object? value, string? rawValue)
    {
        // Null values are valid (use [CsvRequired] for null validation)
        if (value is null)
        {
            return true;
        }

        var stringValue = value.ToString();
        if (stringValue is null)
        {
            return true;
        }

        var length = stringValue.Length;
        return length >= MinLength && length <= MaxLength;
    }

    /// <inheritdoc/>
    protected override string GetDefaultErrorMessage(string fieldName, object? value)
    {
        if (MinLength == 0)
        {
            return $"'{fieldName}' must not exceed {MaxLength} characters.";
        }

        return $"'{fieldName}' must be between {MinLength} and {MaxLength} characters.";
    }
}
