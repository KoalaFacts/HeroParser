using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HeroParser.AI;

/// <summary>
/// High-performance utility to chunk tabular streams into token-bounded, well-formed JSON array blocks.
/// </summary>
public static class JsonLlmChunker
{
    /// <summary>
    /// Formats and chunks an asynchronous enumerable of records into well-formed JSON array blocks using AOT-safe JsonTypeInfo.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The source record stream.</param>
    /// <param name="jsonTypeInfo">The source-generated JSON type metadata for AOT compliance.</param>
    /// <param name="options">Options for chunk sizes and token counters.</param>
    /// <returns>An asynchronous stream of <see cref="LlmChunk"/> objects.</returns>
    public static async IAsyncEnumerable<LlmChunk> ToJsonLlmChunksAsync<T>(
        this IAsyncEnumerable<T> source,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        LlmChunkOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        options ??= new LlmChunkOptions();
        var tokenCounter = options.TokenCounter ?? DefaultTokenCounter;

        var currentBatch = new List<string>();
        int startRow = 1;
        int currentRow = 0;

        await foreach (var record in source)
        {
            currentRow++;
            string recordJson = JsonSerializer.Serialize(record, jsonTypeInfo);

            if (currentBatch.Count > 0)
            {
                // Build proposed JSON array to check token count
                string proposedJson = BuildJsonArray(currentBatch, recordJson);
                int proposedTokens = tokenCounter(proposedJson);

                if (proposedTokens > options.MaxTokensPerChunk)
                {
                    // Yield current chunk
                    string currentJson = BuildJsonArray(currentBatch, null);
                    int currentTokens = tokenCounter(currentJson);

                    yield return new LlmChunk
                    {
                        Content = currentJson,
                        TokenCount = currentTokens,
                        StartRow = startRow,
                        EndRow = currentRow - 1
                    };

                    currentBatch.Clear();
                    startRow = currentRow;
                }
            }

            currentBatch.Add(recordJson);
        }

        if (currentBatch.Count > 0)
        {
            string finalJson = BuildJsonArray(currentBatch, null);
            int finalTokens = tokenCounter(finalJson);

            yield return new LlmChunk
            {
                Content = finalJson,
                TokenCount = finalTokens,
                StartRow = startRow,
                EndRow = currentRow
            };
        }
    }

    /// <summary>
    /// Formats and chunks an asynchronous enumerable of records into well-formed JSON array blocks.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The source record stream.</param>
    /// <param name="options">Options for chunk sizes and token counters.</param>
    /// <returns>An asynchronous stream of <see cref="LlmChunk"/> objects.</returns>
    [RequiresUnreferencedCode("JSON serialization without a JsonTypeInfo<T> uses reflection. Use an overload or annotate custom options.")]
    [RequiresDynamicCode("JSON serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static async IAsyncEnumerable<LlmChunk> ToJsonLlmChunksAsync<T>(
        this IAsyncEnumerable<T> source,
        LlmChunkOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new LlmChunkOptions();
        var tokenCounter = options.TokenCounter ?? DefaultTokenCounter;

        var currentBatch = new List<string>();
        int startRow = 1;
        int currentRow = 0;

        await foreach (var record in source)
        {
            currentRow++;
            string recordJson = JsonSerializer.Serialize(record);

            if (currentBatch.Count > 0)
            {
                // Build proposed JSON array to check token count
                // Proposed format: [r1,r2,...,rn,recordJson]
                string proposedJson = BuildJsonArray(currentBatch, recordJson);
                int proposedTokens = tokenCounter(proposedJson);

                if (proposedTokens > options.MaxTokensPerChunk)
                {
                    // Yield current chunk
                    string currentJson = BuildJsonArray(currentBatch, null);
                    int currentTokens = tokenCounter(currentJson);

                    yield return new LlmChunk
                    {
                        Content = currentJson,
                        TokenCount = currentTokens,
                        StartRow = startRow,
                        EndRow = currentRow - 1
                    };

                    currentBatch.Clear();
                    startRow = currentRow;
                }
            }

            currentBatch.Add(recordJson);
        }

        if (currentBatch.Count > 0)
        {
            string finalJson = BuildJsonArray(currentBatch, null);
            int finalTokens = tokenCounter(finalJson);

            yield return new LlmChunk
            {
                Content = finalJson,
                TokenCount = finalTokens,
                StartRow = startRow,
                EndRow = currentRow
            };
        }
    }

    private static string BuildJsonArray(List<string> items, string? additionalItem)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < items.Count; i++)
        {
            sb.Append(items[i]);
            if (i < items.Count - 1 || additionalItem != null)
            {
                sb.Append(',');
            }
        }
        if (additionalItem != null)
        {
            sb.Append(additionalItem);
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static int DefaultTokenCounter(string text)
    {
        // standard LLM token estimation heuristic: 1 token ~ 4 characters in English
        return (int)Math.Ceiling(text.Length / 4.0);
    }
}
