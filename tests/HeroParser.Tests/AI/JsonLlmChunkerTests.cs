using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using HeroParser.AI;
using Xunit;

namespace HeroParser.Tests.AI;

public sealed class ChunkerRecord
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

public class JsonLlmChunkerTests
{

    [Fact]
    public async Task ToJsonLlmChunksAsync_CreatesValidJsonArray()
    {
        // Arrange
        var records = new List<ChunkerRecord>
        {
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 25 }
        };
        var source = ToAsyncEnumerable(records);

        // Act
        var chunks = await source.ToJsonLlmChunksAsync(new LlmChunkOptions
        {
            MaxTokensPerChunk = 1000
        }).ToListAsync();

        // Assert
        Assert.Single(chunks);
        var chunk = chunks[0];
        Assert.Equal(1, chunk.StartRow);
        Assert.Equal(2, chunk.EndRow);

        // Parse chunk content back to check validity
        var parsed = JsonSerializer.Deserialize<List<ChunkerRecord>>(chunk.Content);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Alice", parsed[0].Name);
        Assert.Equal(30, parsed[0].Age);
        Assert.Equal("Bob", parsed[1].Name);
        Assert.Equal(25, parsed[1].Age);
    }

    [Fact]
    public async Task ToJsonLlmChunksAsync_RespectsMaxTokens()
    {
        // Arrange
        var records = new List<ChunkerRecord>
        {
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 25 },
            new() { Name = "Charlie", Age = 35 }
        };
        var source = ToAsyncEnumerable(records);

        // Act
        // Small token limit (e.g., 15) to force splitting
        var chunks = await source.ToJsonLlmChunksAsync(new LlmChunkOptions
        {
            MaxTokensPerChunk = 15
        }).ToListAsync();

        // Assert
        Assert.True(chunks.Count > 1, $"Expected multiple chunks, got {chunks.Count}");
        foreach (var chunk in chunks)
        {
            var parsed = JsonSerializer.Deserialize<List<ChunkerRecord>>(chunk.Content);
            Assert.NotNull(parsed);
            Assert.True(parsed.Count > 0);
        }
    }

    [Fact]
    public async Task ToJsonLlmChunksAsync_HandlesRowExceedingTokenBudget()
    {
        // Arrange
        var records = new List<ChunkerRecord>
        {
            new() { Name = "Alice Smith Long Name", Age = 30 }
        };
        var source = ToAsyncEnumerable(records);

        // Act
        var chunks = await source.ToJsonLlmChunksAsync(new LlmChunkOptions
        {
            MaxTokensPerChunk = 1
        }).ToListAsync();

        // Assert
        Assert.Single(chunks);
        var parsed = JsonSerializer.Deserialize<List<ChunkerRecord>>(chunks[0].Content);
        Assert.NotNull(parsed);
        Assert.Single(parsed);
    }

    [Fact]
    public async Task ToJsonLlmChunksAsync_ThrowsOnNullSource()
    {
        IAsyncEnumerable<ChunkerRecord> nullSource = null!;
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in nullSource.ToJsonLlmChunksAsync()) { }
        });
    }

    [Fact]
    public async Task ToJsonLlmChunksAsync_WithJsonTypeInfo_WorksCleanly()
    {
        // Arrange
        var records = new List<ChunkerRecord>
        {
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 25 }
        };
        var source = ToAsyncEnumerable(records);

        // Act
        var chunks = await source.ToJsonLlmChunksAsync(
            TestJsonContext.Default.ChunkerRecord,
            new LlmChunkOptions { MaxTokensPerChunk = 1000 }
        ).ToListAsync();

        // Assert
        Assert.Single(chunks);
        var chunk = chunks[0];
        Assert.Equal(1, chunk.StartRow);
        Assert.Equal(2, chunk.EndRow);

        var parsed = JsonSerializer.Deserialize(chunk.Content, TestJsonContext.Default.ListChunkerRecord);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Alice", parsed[0].Name);
        Assert.Equal(30, parsed[0].Age);
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

[System.Text.Json.Serialization.JsonSerializable(typeof(ChunkerRecord))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<ChunkerRecord>))]
internal partial class TestJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
