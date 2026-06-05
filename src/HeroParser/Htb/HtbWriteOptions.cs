namespace HeroParser.Htbs;

/// <summary>
/// Action to take when a record fails to serialize to the HTB stream.
/// </summary>
public enum HtbSerializeErrorAction
{
    /// <summary>Skip the current record and continue writing.</summary>
    SkipRow,

    /// <summary>Rethrow the exception and stop writing.</summary>
    Throw
}

/// <summary>
/// Context information for an HTB serialization error.
/// </summary>
public readonly record struct HtbSerializeErrorContext
{
    /// <summary>Gets the 0-based index of the record.</summary>
    public long RecordIndex { get; init; }

    /// <summary>Gets the name of the property/member where the error occurred, if applicable.</summary>
    public string? MemberName { get; init; }

    /// <summary>Gets the target serialization type, when available.</summary>
    public Type? TargetType { get; init; }
}

/// <summary>
/// Callback invoked when a record fails to serialize to the HTB stream.
/// </summary>
/// <param name="context">Information about the failing record.</param>
/// <param name="exception">The exception thrown during serialization.</param>
/// <returns>The action to take.</returns>
public delegate HtbSerializeErrorAction HtbSerializeErrorHandler(
    HtbSerializeErrorContext context,
    Exception exception);

/// <summary>
/// Configures how HeroParser writes High-Throughput Tabular Binary (HTB) data.
/// </summary>
public sealed record HtbWriteOptions
{
    /// <summary>
    /// Gets or sets the maximum number of records that may be written.
    /// Exceeding this raises an <see cref="HtbException"/> with <see cref="HtbErrorCode.LimitExceeded"/>.
    /// </summary>
    public int? MaxRowCount { get; init; }

    /// <summary>
    /// Gets or sets the maximum output size in bytes (DoS protection).
    /// Exceeding this raises an <see cref="HtbException"/> with <see cref="HtbErrorCode.LimitExceeded"/>.
    /// </summary>
    public long? MaxOutputSize { get; init; }

    /// <summary>
    /// Gets or sets a per-record serialization error handler.
    /// </summary>
    public HtbSerializeErrorHandler? OnError { get; init; }

    /// <summary>
    /// Gets or sets an optional progress reporter.
    /// </summary>
    public IProgress<HtbWriteProgress>? Progress { get; init; }

    /// <summary>
    /// Gets or sets the row interval between progress callbacks (default 1000).
    /// </summary>
    public int ProgressIntervalRows { get; init; } = 1000;

    /// <summary>
    /// Gets the default options instance.
    /// </summary>
    public static HtbWriteOptions Default { get; } = new();

    /// <summary>
    /// Validates the option set and throws when an invalid value is detected.
    /// </summary>
    internal void Validate()
    {
        if (MaxRowCount is <= 0)
        {
            throw new HtbException(
                HtbErrorCode.SerializationError,
                $"MaxRowCount must be positive when specified, got {MaxRowCount}.");
        }

        if (MaxOutputSize is <= 0)
        {
            throw new HtbException(
                HtbErrorCode.SerializationError,
                $"MaxOutputSize must be positive when specified, got {MaxOutputSize}.");
        }

        if (ProgressIntervalRows <= 0)
        {
            throw new HtbException(
                HtbErrorCode.SerializationError,
                $"ProgressIntervalRows must be positive, got {ProgressIntervalRows}");
        }
    }
}
