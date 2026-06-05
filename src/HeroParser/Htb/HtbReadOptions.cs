using HeroParser.Validation;

namespace HeroParser.Htbs;

/// <summary>
/// Action to take when a record in an HTB stream fails to deserialize.
/// </summary>
public enum HtbDeserializeErrorAction
{
    /// <summary>Skip the current record and continue reading.</summary>
    SkipRecord,

    /// <summary>Rethrow the exception and stop reading.</summary>
    Throw
}

/// <summary>
/// Context information for an HTB deserialization error.
/// </summary>
public readonly record struct HtbDeserializeErrorContext
{
    /// <summary>Gets the 0-based index of the record.</summary>
    public long RecordIndex { get; init; }

    /// <summary>Gets the name of the property/member where the error occurred, if applicable.</summary>
    public string? MemberName { get; init; }

    /// <summary>Gets the target deserialization type, when available.</summary>
    public Type? TargetType { get; init; }
}

/// <summary>
/// Callback invoked when a record in an HTB stream fails to deserialize.
/// </summary>
/// <param name="context">Information about the failing record.</param>
/// <param name="exception">The exception thrown during deserialization.</param>
/// <returns>The action to take.</returns>
public delegate HtbDeserializeErrorAction HtbDeserializeErrorHandler(
    HtbDeserializeErrorContext context,
    Exception exception);

/// <summary>
/// Configures how HeroParser reads High-Throughput Tabular Binary (HTB) data.
/// </summary>
public sealed record HtbReadOptions
{
    /// <summary>
    /// Gets or sets the maximum number of records that may be read (default 1,000,000).
    /// Exceeding this raises an <see cref="HtbException"/> with <see cref="HtbErrorCode.LimitExceeded"/>.
    /// </summary>
    public int MaxRowCount { get; init; } = 1_000_000;

    /// <summary>
    /// Gets or sets the number of leading data records to skip (default 0).
    /// </summary>
    public int SkipRows { get; init; } = 0;

    /// <summary>
    /// Gets or sets the validation mode (default <see cref="ValidationMode.Strict"/>).
    /// </summary>
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Strict;

    /// <summary>
    /// Gets or sets an optional error handler that decides whether to skip or rethrow on deserialization failures.
    /// </summary>
    public HtbDeserializeErrorHandler? OnError { get; init; }

    /// <summary>
    /// Gets or sets an optional progress reporter.
    /// </summary>
    public IProgress<HtbProgress>? Progress { get; init; }

    /// <summary>
    /// Gets or sets the row interval between progress callbacks (default 1000).
    /// </summary>
    public int ProgressIntervalRows { get; init; } = 1000;

    /// <summary>
    /// Gets the default options instance.
    /// </summary>
    public static HtbReadOptions Default { get; } = new();

    /// <summary>
    /// Validates the option set and throws when an invalid value is detected.
    /// </summary>
    internal void Validate()
    {
        if (MaxRowCount <= 0)
        {
            throw new HtbException(
                HtbErrorCode.SerializationError,
                $"MaxRowCount must be positive, got {MaxRowCount}");
        }

        if (SkipRows < 0)
        {
            throw new HtbException(
                HtbErrorCode.SerializationError,
                $"SkipRows cannot be negative, got {SkipRows}");
        }

        if (ProgressIntervalRows <= 0)
        {
            throw new HtbException(
                HtbErrorCode.SerializationError,
                $"ProgressIntervalRows must be positive, got {ProgressIntervalRows}");
        }
    }
}
