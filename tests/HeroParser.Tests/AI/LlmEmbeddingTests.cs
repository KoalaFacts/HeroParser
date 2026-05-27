using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.AI;
using Xunit;

namespace HeroParser.Tests.AI;

public class LlmEmbeddingTests
{
    private sealed class LlmEmbeddingTestRecord
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    [Fact]
    public async Task ToLlmEmbeddingsAsync_SingleGenerator_WorksCorrectly()
    {
        // Arrange
        var records = new List<LlmEmbeddingTestRecord>
        {
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 25 }
        };
        var source = ToAsyncEnumerable(records);

        static ValueTask<float[]> generator(string text, CancellationToken ct)
        {
            return new([1.0f, 2.0f, 3.0f]);
        }

        // Act
        var result = await source.ToLlmEmbeddingsAsync(generator, cancellationToken: TestContext.Current.CancellationToken).ToListAsync();

        // Assert
        Assert.Single(result); // One chunk because budget is large
        var chunk = result[0];
        Assert.Equal([1.0f, 2.0f, 3.0f], chunk.Embedding);
        Assert.Contains("Alice", chunk.Chunk.Content);
        Assert.Contains("Bob", chunk.Chunk.Content);
    }

    [Fact]
    public async Task ToLlmEmbeddingsAsync_BatchedGenerator_WorksCorrectly()
    {
        // Arrange
        var records = new List<LlmEmbeddingTestRecord>
        {
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 25 },
            new() { Name = "Charlie", Age = 35 }
        };
        var source = ToAsyncEnumerable(records);

        static ValueTask<IReadOnlyList<float[]>> generator(IReadOnlyList<string> texts, CancellationToken ct)
        {
            var list = new List<float[]>();
            foreach (var t in texts)
            {
                list.Add([1.0f, 2.0f]);
            }
            return new(list);
        }

        // Act
        // Small max tokens per chunk to force multiple chunks (each row ~4-5 tokens)
        var result = await source.ToLlmEmbeddingsAsync(generator, new LlmChunkOptions
        {
            MaxTokensPerChunk = 5,
            RepeatHeaders = false
        }, batchSize: 2, cancellationToken: TestContext.Current.CancellationToken).ToListAsync();

        // Assert
        Assert.Equal(3, result.Count);
        foreach (var chunk in result)
        {
            Assert.Equal([1.0f, 2.0f], chunk.Embedding);
        }
    }

    [Fact]
    public async Task ToLlmEmbeddingsAsync_ThrowsOnNullSourceOrGenerator()
    {
        IAsyncEnumerable<LlmEmbeddingTestRecord> nullSource = null!;
        static ValueTask<float[]> singleGen(string txt, CancellationToken ct) => new([]);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in nullSource.ToLlmEmbeddingsAsync(singleGen, cancellationToken: TestContext.Current.CancellationToken)) { }
        });

        var source = ToAsyncEnumerable(new List<LlmEmbeddingTestRecord>());
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in source.ToLlmEmbeddingsAsync((Func<string, CancellationToken, ValueTask<float[]>>)null!, cancellationToken: TestContext.Current.CancellationToken)) { }
        });

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in source.ToLlmEmbeddingsAsync((Func<IReadOnlyList<string>, CancellationToken, ValueTask<IReadOnlyList<float[]>>>)null!, cancellationToken: TestContext.Current.CancellationToken)) { }
        });
    }

    [Fact]
    public async Task ToLlmEmbeddingsAsync_ThrowsOnInvalidBatchSize()
    {
        var source = ToAsyncEnumerable(new List<LlmEmbeddingTestRecord>());
        static ValueTask<IReadOnlyList<float[]>> batchGen(IReadOnlyList<string> txts, CancellationToken ct) => new([]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in source.ToLlmEmbeddingsAsync(batchGen, batchSize: 0, cancellationToken: TestContext.Current.CancellationToken)) { }
        });
    }

    [Fact]
    public async Task ToLlmEmbeddingsAsync_ThrowsOnMismatchedEmbeddings()
    {
        var records = new List<LlmEmbeddingTestRecord>
        {
            new() { Name = "Alice", Age = 30 }
        };
        var source = ToAsyncEnumerable(records);

        static ValueTask<IReadOnlyList<float[]>> generator(IReadOnlyList<string> texts, CancellationToken ct)
        {
            // Return empty list which mismatches input count of 1
            return new([]);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in source.ToLlmEmbeddingsAsync(generator, cancellationToken: TestContext.Current.CancellationToken)) { }
        });
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
