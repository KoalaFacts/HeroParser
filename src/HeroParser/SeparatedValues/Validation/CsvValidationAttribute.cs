namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Base class for CSV field validation attributes.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public abstract class CsvValidationAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the error message to display when validation fails.
    /// </summary>
    /// <remarks>
    /// The message may contain placeholders: {0} = field name, {1} = value.
    /// </remarks>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Validates the specified value.
    /// </summary>
    /// <param name="value">The value to validate (may be null).</param>
    /// <param name="rawValue">The raw string value from the CSV.</param>
    /// <returns>True if valid; otherwise, false.</returns>
    public abstract bool IsValid(object? value, string? rawValue);

    /// <summary>
    /// Formats the error message for a validation failure.
    /// </summary>
    /// <param name="fieldName">The name of the field that failed validation.</param>
    /// <param name="value">The value that failed validation.</param>
    /// <returns>The formatted error message.</returns>
    public virtual string FormatErrorMessage(string fieldName, object? value)
    {
        if (ErrorMessage is not null)
        {
            return string.Format(ErrorMessage, fieldName, value);
        }

        return GetDefaultErrorMessage(fieldName, value);
    }

    /// <summary>
    /// Gets the default error message for this validation type.
    /// </summary>
    protected abstract string GetDefaultErrorMessage(string fieldName, object? value);
}
