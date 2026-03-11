namespace HeroParser.Validation;

/// <summary>
/// Describes a single field-level validation failure produced during CSV or fixed-width record mapping.
/// </summary>
public readonly struct ValidationError
{
    /// <summary>Gets the 1-based row number in the source file where the error occurred.</summary>
    public int RowNumber { get; init; }

    /// <summary>Gets the zero-based column index of the field that failed validation.</summary>
    public int ColumnIndex { get; init; }

    /// <summary>Gets the column name from the header row, or <see langword="null"/> when no header is present.</summary>
    public string? ColumnName { get; init; }

    /// <summary>Gets the name of the target property or field on the record type.</summary>
    public string PropertyName { get; init; }

    /// <summary>Gets the name of the validation rule that was violated (e.g. "Required", "MaxLength").</summary>
    public string Rule { get; init; }

    /// <summary>Gets a human-readable description of why the validation failed.</summary>
    public string Message { get; init; }

    /// <summary>Gets the raw string value read from the source, or <see langword="null"/> when the column was absent.</summary>
    public string? RawValue { get; init; }
}
