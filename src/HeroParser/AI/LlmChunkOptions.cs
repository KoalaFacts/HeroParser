using System;

namespace HeroParser.AI;

/// <summary>
/// Options for configuring tabular chunk serialization for LLMs.
/// </summary>
public sealed class LlmChunkOptions
{
    /// <summary>
    /// Gets or sets the maximum number of tokens allowed per chunk. Defaults to 1000.
    /// </summary>
    public int MaxTokensPerChunk { get; set; } = 1000;

    /// <summary>
    /// Gets or sets whether to repeat headers at the top of every chunk. Defaults to true.
    /// </summary>
    public bool RepeatHeaders { get; set; } = true;

    /// <summary>
    /// Gets or sets a custom template for formatting a record into a natural language sentence
    /// (e.g., "Employee {Name} works in {Department}").
    /// If null, the rows will be formatted as a Markdown table.
    /// </summary>
    public string? CustomTemplate { get; set; }

    /// <summary>
    /// Gets or sets a custom token counter delegate. If null, a high-performance heuristic
    /// (approximately 4 characters per token) is used.
    /// </summary>
    public Func<string, int>? TokenCounter { get; set; }
}
