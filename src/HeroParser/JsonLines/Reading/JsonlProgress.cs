namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Progress information surfaced during JSONL reading.
/// </summary>
public readonly record struct JsonlProgress
{
    /// <summary>Gets the 1-based source line number most recently observed.</summary>
    public long LineNumber { get; init; }

    /// <summary>Gets the total number of bytes read so far.</summary>
    public long BytesRead { get; init; }

    /// <summary>Gets the number of records successfully read so far.</summary>
    public long RecordsRead { get; init; }
}
