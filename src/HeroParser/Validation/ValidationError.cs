namespace HeroParser.Validation;

/// <summary>
/// Describes a single field-level validation failure produced during CSV or fixed-width record mapping.
/// </summary>
public readonly struct ValidationError
{
    /// <summary>Gets the 1-based row number in the source file where the error occurred.</summary>
    public int RowNumber { get; init; }

    /// <summary>Gets the zero-based column index of the field that failed validation. For fixed-width data,
    /// this is the zero-based start position of the field.</summary>
    public int ColumnIndex { get; init; }

    /// <summary>Gets the column name from the header row, or <see langword="null"/> when no header is present.</summary>
    public string? ColumnName { get; init; }

    /// <summary>Gets the name of the target property or field on the record type.</summary>
    public string PropertyName { get; init; }

    /// <summary>Gets the name of the validation rule that was violated (e.g. "NotNull", "MaxLength").</summary>
    public string Rule { get; init; }

    /// <summary>Gets a human-readable description of why the validation failed.</summary>
    public string Message { get; init; }

    /// <summary>Gets the raw string value read from the source, or <see langword="null"/> when the column was absent.</summary>
    public string? RawValue { get; init; }

    /// <summary>
    /// Returns a rich diagnostic string containing the row number, column location, property name,
    /// validation rule, message, and raw value.
    /// </summary>
    /// <example>
    /// <c>Row 2, Column 'Amount' (index 1), Property 'Amount': [Range] Value must be between 0 and 100000 (raw: '-1.00')</c>
    /// </example>
    public override string ToString()
    {
        var column = ColumnName != null
            ? $"Column '{ColumnName}' (index {ColumnIndex})"
            : $"Column index {ColumnIndex}";

        var raw = RawValue != null
            ? $" (raw: '{RawValue}')"
            : "";

        return $"Row {RowNumber}, {column}, Property '{PropertyName}': [{Rule}] {Message}{raw}";
    }
}
