namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Action to take when a JSONL line fails to deserialize.
/// </summary>
public enum JsonlDeserializeErrorAction
{
    /// <summary>Skip the current line and continue reading.</summary>
    SkipRecord,

    /// <summary>Rethrow the exception and stop reading.</summary>
    Throw
}

/// <summary>
/// Context information for a JSONL deserialization error.
/// </summary>
public readonly record struct JsonlDeserializeErrorContext
{
    /// <summary>Gets the 1-based source line number where the error occurred.</summary>
    public long LineNumber { get; init; }

    /// <summary>Gets the 0-based index of the record (post-skip and post-empty-line filtering).</summary>
    public long RecordIndex { get; init; }

    /// <summary>Gets the raw line content that failed to parse, when available.</summary>
    public string? RawLine { get; init; }

    /// <summary>Gets the target deserialization type, when available.</summary>
    public Type? TargetType { get; init; }
}

/// <summary>
/// Callback invoked when a JSONL line fails to deserialize.
/// </summary>
/// <param name="context">Information about the failing line.</param>
/// <param name="exception">The exception thrown by the JSON deserializer.</param>
/// <returns>The action to take.</returns>
public delegate JsonlDeserializeErrorAction JsonlDeserializeErrorHandler(
    JsonlDeserializeErrorContext context,
    Exception exception);
