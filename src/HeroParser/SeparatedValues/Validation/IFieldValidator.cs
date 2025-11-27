namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Provides context for field validation.
/// </summary>
public readonly struct CsvValidationContext
{
    /// <summary>
    /// Gets the 1-based row number being validated.
    /// </summary>
    public int Row { get; init; }

    /// <summary>
    /// Gets the 1-based column number being validated.
    /// </summary>
    public int Column { get; init; }

    /// <summary>
    /// Gets the name of the field/property being validated.
    /// </summary>
    public string FieldName { get; init; }

    /// <summary>
    /// Gets the value being validated (may be null).
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Gets the raw string value from the CSV field.
    /// </summary>
    public string? RawValue { get; init; }
}

/// <summary>
/// Result of field validation.
/// </summary>
public readonly struct CsvValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the validation passed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the error message when validation fails.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static CsvValidationResult Success { get; } = new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result with an error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public static CsvValidationResult Failure(string message) => new() { IsValid = false, ErrorMessage = message };
}

/// <summary>
/// Specifies the action to take when a validation error occurs.
/// </summary>
public enum ValidationErrorAction
{
    /// <summary>
    /// Throw an exception (default behavior).
    /// </summary>
    Throw,

    /// <summary>
    /// Skip the current row and continue processing.
    /// </summary>
    SkipRow,

    /// <summary>
    /// Use the default value for the property and continue.
    /// </summary>
    UseDefault
}

/// <summary>
/// Delegate for handling validation errors during record binding.
/// </summary>
/// <param name="context">Context about the validation error.</param>
/// <param name="errorMessage">The validation error message.</param>
/// <returns>The action to take in response to the error.</returns>
public delegate ValidationErrorAction CsvValidationErrorHandler(CsvValidationContext context, string errorMessage);

/// <summary>
/// Defines a field validator that can validate CSV field values.
/// </summary>
public interface IFieldValidator
{
    /// <summary>
    /// Validates a field value.
    /// </summary>
    /// <param name="value">The value to validate (may be null).</param>
    /// <param name="rawValue">The raw string value from the CSV.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    CsvValidationResult Validate(object? value, string? rawValue);
}
