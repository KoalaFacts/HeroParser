namespace HeroParser.Excels.Writing;

/// <summary>
/// Provides context about a serialization error that occurred while writing an Excel record.
/// </summary>
public readonly struct ExcelSerializeErrorContext
{
    /// <summary>
    /// Gets the 1-based row number where the error occurred.
    /// </summary>
    public int Row { get; init; }

    /// <summary>
    /// Gets the 1-based column number where the error occurred.
    /// </summary>
    public int Column { get; init; }

    /// <summary>
    /// Gets the name of the member (property) being serialized.
    /// </summary>
    public string MemberName { get; init; }

    /// <summary>
    /// Gets the declared source type of the property being serialized.
    /// </summary>
    public Type SourceType { get; init; }

    /// <summary>
    /// Gets the value that failed to serialize (may be <see langword="null"/>).
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Gets the exception that caused the serialization failure, or <see langword="null"/> if not caused by an exception.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the name of the worksheet being written when the error occurred.
    /// </summary>
    public string SheetName { get; init; }
}

/// <summary>
/// Specifies the action to take when a serialization error occurs during Excel writing.
/// </summary>
public enum ExcelSerializeErrorAction
{
    /// <summary>
    /// Throw an exception (default behavior).
    /// </summary>
    Throw,

    /// <summary>
    /// Skip the current row entirely and continue with the next record.
    /// </summary>
    SkipRow,

    /// <summary>
    /// Write an empty cell for the failed field and continue writing the row.
    /// </summary>
    WriteEmpty
}

/// <summary>
/// Delegate for handling serialization errors during Excel record writing.
/// </summary>
/// <param name="context">Context about the serialization error.</param>
/// <returns>The action to take in response to the error.</returns>
public delegate ExcelSerializeErrorAction ExcelSerializeErrorHandler(ExcelSerializeErrorContext context);
