namespace HeroParser.Excels.Reading;

/// <summary>
/// Delegate for handling deserialization errors during Excel record reading.
/// </summary>
/// <param name="context">Context information about the error.</param>
/// <param name="exception">The exception that caused the error.</param>
/// <returns>The action to take: skip the record or rethrow the exception.</returns>
public delegate ExcelDeserializeErrorAction ExcelDeserializeErrorHandler(
    ExcelDeserializeErrorContext context,
    Exception exception);

/// <summary>
/// Action to take when a deserialization error occurs during Excel reading.
/// </summary>
public enum ExcelDeserializeErrorAction
{
    /// <summary>Skip the current record and continue reading.</summary>
    SkipRecord,

    /// <summary>Rethrow the exception and stop reading.</summary>
    Throw
}

/// <summary>
/// Context information for a deserialization error that occurred while reading an Excel row.
/// </summary>
public readonly record struct ExcelDeserializeErrorContext
{
    /// <summary>Gets the 1-based row number where the error occurred.</summary>
    public int Row { get; init; }

    /// <summary>Gets the name of the sheet being read.</summary>
    public string SheetName { get; init; }

    /// <summary>Gets the field name that failed to deserialize, if available.</summary>
    public string? FieldName { get; init; }

    /// <summary>Gets the raw cell value that caused the error, if available.</summary>
    public string? RawValue { get; init; }

    /// <summary>Gets the target property type, if available.</summary>
    public Type? TargetType { get; init; }
}
