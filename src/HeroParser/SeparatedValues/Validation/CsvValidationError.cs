namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Represents a validation error found during CSV validation.
/// </summary>
/// <remarks>
/// Thread-Safety: This is an immutable record type and is safe to share across threads.
/// </remarks>
public sealed record CsvValidationError
{
    /// <summary>
    /// Gets the type of validation error.
    /// </summary>
    public CsvValidationErrorType ErrorType { get; init; }

    /// <summary>
    /// Gets the error message describing the validation failure.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Gets the 1-based row number where the error occurred (0 if not applicable).
    /// </summary>
    public int RowNumber { get; init; }

    /// <summary>
    /// Gets the 1-based column number where the error occurred (0 if not applicable).
    /// </summary>
    public int ColumnNumber { get; init; }

    /// <summary>
    /// Gets the expected value or constraint that was violated (if applicable).
    /// </summary>
    public string? Expected { get; init; }

    /// <summary>
    /// Gets the actual value that caused the validation error (if applicable).
    /// </summary>
    public string? Actual { get; init; }
}
