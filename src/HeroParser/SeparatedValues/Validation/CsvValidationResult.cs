namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Represents the result of CSV validation.
/// </summary>
/// <remarks>
/// Thread-Safety: This is an immutable record type and is safe to share across threads.
/// </remarks>
public sealed record CsvValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the CSV passed all validation checks.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets the list of validation errors found. Empty if validation passed.
    /// </summary>
    public IReadOnlyList<CsvValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Gets the total number of rows validated (including header if present).
    /// </summary>
    public int TotalRows { get; init; }

    /// <summary>
    /// Gets the number of columns detected (from header or first data row).
    /// </summary>
    public int ColumnCount { get; init; }

    /// <summary>
    /// Gets the detected or specified delimiter character.
    /// </summary>
    public char Delimiter { get; init; }

    /// <summary>
    /// Gets the list of header names if headers were validated.
    /// </summary>
    public IReadOnlyList<string> Headers { get; init; } = [];
}
