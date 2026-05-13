using System.Runtime.CompilerServices;

namespace HeroParser.Streaming;

/// <summary>
/// Extensions over <see cref="IAsyncEnumerable{T}"/> for AI/ML streaming pipelines.
/// </summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>
    /// Groups the source sequence into batches of <paramref name="size"/> elements. Yields a partial final
    /// batch when the source ends mid-batch.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The async sequence to batch.</param>
    /// <param name="size">The target batch size (must be positive). Typical values: 100–2048 for embedding APIs.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <remarks>
    /// Designed for embedding API pipelines: OpenAI, Voyage, Cohere, and Anthropic all accept inputs in
    /// batches. Use <c>FromFileAsync(...).BatchAsync(100)</c> to stream CSV/JSONL records into a vector database.
    /// Each yielded batch is a freshly allocated list — callers may retain it beyond the next iteration.
    /// </remarks>
    public static async IAsyncEnumerable<IReadOnlyList<T>> BatchAsync<T>(
        this IAsyncEnumerable<T> source,
        int size,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Batch size must be positive.");

        List<T> batch = new(size);
        await foreach (T item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(item);
            if (batch.Count >= size)
            {
                yield return batch;
                batch = new List<T>(size);
            }
        }

        if (batch.Count > 0)
            yield return batch;
    }
}
