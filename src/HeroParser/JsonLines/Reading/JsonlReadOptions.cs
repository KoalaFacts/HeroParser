using System.Text.Json;
using HeroParser.Validation;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Configures how HeroParser reads JSONL (JSON Lines) data.
/// </summary>
/// <remarks>
/// Defaults: line splitting on <c>\n</c> (treats <c>\r\n</c> as <c>\n</c>), UTF-8 BOM stripped,
/// empty lines skipped, max line size of 1 MiB.
/// </remarks>
public sealed record JsonlReadOptions
{
    /// <summary>
    /// Gets or sets the <see cref="System.Text.Json.JsonSerializerOptions"/> to use for deserialization.
    /// When <see langword="null"/>, defaults to <see cref="JsonSerializerOptions.Default"/>.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; init; }

    /// <summary>
    /// Gets or sets the maximum allowed length of a single JSONL line in bytes (default 1 MiB).
    /// Exceeding this raises a <see cref="JsonlException"/> with <see cref="JsonlErrorCode.LineTooLong"/>.
    /// </summary>
    public int MaxLineSizeBytes { get; init; } = 1 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of records that may be read (default 1,000,000).
    /// Exceeding this raises a <see cref="JsonlException"/> with <see cref="JsonlErrorCode.TooManyRows"/>.
    /// </summary>
    public int MaxRowCount { get; init; } = 1_000_000;

    /// <summary>
    /// Gets or sets a value indicating whether blank lines are silently skipped (default <see langword="true"/>).
    /// </summary>
    public bool SkipEmptyLines { get; init; } = true;

    /// <summary>
    /// Gets or sets the number of leading data records to skip (default 0).
    /// </summary>
    public int SkipRows { get; init; } = 0;

    /// <summary>
    /// Gets or sets the validation mode (default <see cref="ValidationMode.Strict"/>).
    /// </summary>
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Strict;

    /// <summary>
    /// Gets or sets an optional error handler that decides whether to skip or rethrow on per-line failures.
    /// </summary>
    public JsonlDeserializeErrorHandler? OnError { get; init; }

    /// <summary>
    /// Gets or sets an optional progress reporter.
    /// </summary>
    public IProgress<JsonlProgress>? Progress { get; init; }

    /// <summary>
    /// Gets or sets the row interval between progress callbacks (default 1000).
    /// </summary>
    public int ProgressIntervalRows { get; init; } = 1000;

    /// <summary>
    /// Gets the default options instance.
    /// </summary>
    public static JsonlReadOptions Default { get; } = new();

    /// <summary>
    /// Validates the option set and throws when an invalid value is detected.
    /// </summary>
    internal void Validate()
    {
        if (MaxLineSizeBytes <= 0)
        {
            throw new JsonlException(
                JsonlErrorCode.InvalidOptions,
                $"MaxLineSizeBytes must be positive, got {MaxLineSizeBytes}");
        }

        if (MaxRowCount <= 0)
        {
            throw new JsonlException(
                JsonlErrorCode.InvalidOptions,
                $"MaxRowCount must be positive, got {MaxRowCount}");
        }

        if (SkipRows < 0)
        {
            throw new JsonlException(
                JsonlErrorCode.InvalidOptions,
                $"SkipRows cannot be negative, got {SkipRows}");
        }

        if (ProgressIntervalRows <= 0)
        {
            throw new JsonlException(
                JsonlErrorCode.InvalidOptions,
                $"ProgressIntervalRows must be positive, got {ProgressIntervalRows}");
        }
    }
}
