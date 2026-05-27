using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.AI;

/// <summary>
/// Pairs a semantic tabular chunk with its generated vector embedding.
/// </summary>
/// <param name="Chunk">The semantic data chunk.</param>
/// <param name="Embedding">The float vector representing the chunk's semantic embedding.</param>
public sealed record LlmChunkWithEmbedding(LlmChunk Chunk, float[] Embedding);

/// <summary>
/// High-performance AI embedding extensions for streaming tabular data directly into vector representations.
/// </summary>
public static class LlmEmbeddingExtensions
{
    /// <summary>
    /// Streams record chunks, batches them, and pairs them with vector embeddings using a batched generator delegate.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The source record stream.</param>
    /// <param name="batchEmbeddingGenerator">The batched embedding generator delegate.</param>
    /// <param name="options">Optional chunking options.</param>
    /// <param name="batchSize">The maximum number of chunks to embed in a single request. Defaults to 16.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of chunks paired with vector embeddings.</returns>
    public static async IAsyncEnumerable<LlmChunkWithEmbedding> ToLlmEmbeddingsAsync<T>(
        this IAsyncEnumerable<T> source,
        Func<IReadOnlyList<string>, CancellationToken, ValueTask<IReadOnlyList<float[]>>> batchEmbeddingGenerator,
        LlmChunkOptions? options = null,
        int batchSize = 16,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(batchEmbeddingGenerator);
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        var chunks = source.ToLlmChunksAsync(options);
        var batch = new List<LlmChunk>(batchSize);

        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(chunk);
            if (batch.Count >= batchSize)
            {
                var emitted = await EmitBatchAsync(batch, batchEmbeddingGenerator, cancellationToken).ConfigureAwait(false);
                foreach (var item in emitted)
                {
                    yield return item;
                }
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            var emitted = await EmitBatchAsync(batch, batchEmbeddingGenerator, cancellationToken).ConfigureAwait(false);
            foreach (var item in emitted)
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Streams record chunks and pairs them with vector embeddings using a single-text generator delegate.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The source record stream.</param>
    /// <param name="embeddingGenerator">The embedding generator delegate.</param>
    /// <param name="options">Optional chunking options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of chunks paired with vector embeddings.</returns>
    public static IAsyncEnumerable<LlmChunkWithEmbedding> ToLlmEmbeddingsAsync<T>(
        this IAsyncEnumerable<T> source,
        Func<string, CancellationToken, ValueTask<float[]>> embeddingGenerator,
        LlmChunkOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        return source.ToLlmEmbeddingsAsync(
            async (texts, ct) =>
            {
                var result = new float[texts.Count][];
                for (int i = 0; i < texts.Count; i++)
                {
                    result[i] = await embeddingGenerator(texts[i], ct).ConfigureAwait(false);
                }
                return result;
            },
            options,
            batchSize: 1,
            cancellationToken);
    }

    private static async Task<List<LlmChunkWithEmbedding>> EmitBatchAsync(
        List<LlmChunk> batch,
        Func<IReadOnlyList<string>, CancellationToken, ValueTask<IReadOnlyList<float[]>>> batchEmbeddingGenerator,
        CancellationToken cancellationToken)
    {
        var contents = new string[batch.Count];
        for (int i = 0; i < batch.Count; i++)
        {
            contents[i] = batch[i].Content;
        }

        var embeddings = await batchEmbeddingGenerator(contents, cancellationToken).ConfigureAwait(false);
        if (embeddings == null || embeddings.Count != batch.Count)
        {
            throw new InvalidOperationException("The embedding generator returned a null or mismatched number of embeddings.");
        }

        var result = new List<LlmChunkWithEmbedding>(batch.Count);
        for (int i = 0; i < batch.Count; i++)
        {
            result.Add(new LlmChunkWithEmbedding(batch[i], embeddings[i]));
        }
        return result;
    }
}
