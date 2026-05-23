using System.Text;
using System.Text.Json;

namespace HeroParser.JsonLines.Writing;

/// <summary>
/// Configures how HeroParser writes JSONL (JSON Lines) data.
/// </summary>
public sealed record JsonlWriteOptions
{
    /// <summary>
    /// Gets or sets the <see cref="JsonSerializerOptions"/> used for serialization.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; init; }

    /// <summary>
    /// Gets or sets the line separator (default <c>"\n"</c>).
    /// </summary>
    public string NewLine { get; init; } = "\n";

    /// <summary>
    /// Gets or sets the encoding used for the output stream (default UTF-8 without BOM).
    /// </summary>
    public Encoding? Encoding { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of records that may be written.
    /// </summary>
    public int? MaxRowCount { get; init; }

    /// <summary>
    /// Gets or sets the maximum output size in bytes (DoS protection).
    /// </summary>
    public long? MaxOutputSize { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether to emit a final newline after the last record (default <see langword="false"/>).
    /// </summary>
    public bool WriteFinalNewline { get; init; }

    /// <summary>
    /// Gets or sets a per-record serialization error handler.
    /// </summary>
    public JsonlSerializeErrorHandler? OnError { get; init; }

    /// <summary>
    /// Gets the default options instance.
    /// </summary>
    public static JsonlWriteOptions Default { get; } = new();

    /// <summary>
    /// Validates the option set and throws when an invalid value is detected.
    /// </summary>
    internal void Validate()
    {
        if (string.IsNullOrEmpty(NewLine))
        {
            throw new JsonlException(
                JsonlErrorCode.InvalidOptions,
                "NewLine must not be empty.");
        }

        if (MaxRowCount is <= 0)
        {
            throw new JsonlException(
                JsonlErrorCode.InvalidOptions,
                $"MaxRowCount must be positive when specified, got {MaxRowCount}.");
        }

        if (MaxOutputSize is <= 0)
        {
            throw new JsonlException(
                JsonlErrorCode.InvalidOptions,
                $"MaxOutputSize must be positive when specified, got {MaxOutputSize}.");
        }
    }
}
