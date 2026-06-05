namespace HeroParser.Htbs;

/// <summary>
/// Progress information surfaced during HTB reading.
/// </summary>
public readonly record struct HtbProgress
{
    /// <summary>Gets the total number of bytes read so far.</summary>
    public long BytesRead { get; init; }

    /// <summary>Gets the number of records successfully read so far.</summary>
    public long RecordsRead { get; init; }
}

/// <summary>
/// Progress information surfaced during HTB writing.
/// </summary>
public readonly record struct HtbWriteProgress
{
    /// <summary>Gets the total number of bytes written so far.</summary>
    public long BytesWritten { get; init; }

    /// <summary>Gets the number of records successfully written so far.</summary>
    public long RecordsWritten { get; init; }
}
