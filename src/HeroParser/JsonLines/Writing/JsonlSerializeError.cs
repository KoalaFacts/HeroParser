namespace HeroParser.JsonLines.Writing;

/// <summary>
/// Action to take when a record fails to serialize during JSONL writing.
/// </summary>
public enum JsonlSerializeErrorAction
{
    /// <summary>Skip the current record and continue writing.</summary>
    SkipRecord,

    /// <summary>Rethrow the exception and stop writing.</summary>
    Throw
}

/// <summary>
/// Context information for a JSONL serialization error.
/// </summary>
public readonly record struct JsonlSerializeErrorContext
{
    /// <summary>Gets the 0-based index of the failing record within the input sequence.</summary>
    public long RecordIndex { get; init; }

    /// <summary>Gets the source type being serialized, when available.</summary>
    public Type? SourceType { get; init; }
}

/// <summary>
/// Callback invoked when a record fails to serialize.
/// </summary>
public delegate JsonlSerializeErrorAction JsonlSerializeErrorHandler(
    JsonlSerializeErrorContext context,
    Exception exception);
