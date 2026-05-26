namespace HeroParser.AI;

/// <summary>
/// Represents a structured semantic chunk of tabular data prepared for LLM ingestion.
/// </summary>
public sealed class LlmChunk
{
    /// <summary>
    /// Gets the formatted text content (e.g., Markdown table segment or natural language text).
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Gets the estimated or exact token count of the content.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Gets the starting 1-based row index in the source dataset.
    /// </summary>
    public int StartRow { get; init; }

    /// <summary>
    /// Gets the ending 1-based row index in the source dataset.
    /// </summary>
    public int EndRow { get; init; }
}
