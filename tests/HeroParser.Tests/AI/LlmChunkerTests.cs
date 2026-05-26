using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HeroParser.AI;
using Xunit;

namespace HeroParser.Tests.AI;

public class LlmChunkerTests
{
    private sealed class TestEmployee
    {
        [TabularMap(Name = "Full Name")]
        public string Name { get; set; } = string.Empty;

        [TabularMap(Name = "Job Title")]
        public string Title { get; set; } = string.Empty;

        public double Salary { get; set; }
    }

    [Fact]
    public async Task ToLlmChunksAsync_MarkdownFormat_CreatesValidTable()
    {
        // Arrange
        var employees = new List<TestEmployee>
        {
            new() { Name = "Alice Smith", Title = "Lead Developer", Salary = 120000.0 },
            new() { Name = "Bob Jones", Title = "Senior Designer", Salary = 95000.0 }
        };

        var source = ToAsyncEnumerable(employees);

        // Act
        var chunks = await source.ToLlmChunksAsync(new LlmChunkOptions
        {
            MaxTokensPerChunk = 1000,
            RepeatHeaders = true
        }).ToListAsync();

        // Assert
        Assert.Single(chunks);
        var chunk = chunks[0];
        Assert.Equal(1, chunk.StartRow);
        Assert.Equal(2, chunk.EndRow);

        var lines = chunk.Content.Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal("| Full Name | Job Title | Salary |", lines[0]);
        Assert.Equal("| --- | --- | --- |", lines[1]);
        Assert.Equal("| Alice Smith | Lead Developer | 120000 |", lines[2]);
        Assert.Equal("| Bob Jones | Senior Designer | 95000 |", lines[3]);
    }

    [Fact]
    public async Task ToLlmChunksAsync_RespectsMaxTokensAndRepeatsHeaders()
    {
        // Arrange
        var employees = new List<TestEmployee>
        {
            new() { Name = "Alice Smith", Title = "Lead Developer", Salary = 120000.0 },
            new() { Name = "Bob Jones", Title = "Senior Designer", Salary = 95000.0 },
            new() { Name = "Charlie Brown", Title = "Intern", Salary = 45000.0 }
        };

        var source = ToAsyncEnumerable(employees);

        // Act
        // Set small MaxTokensPerChunk so that each data row has to be in its own chunk.
        var chunks = await source.ToLlmChunksAsync(new LlmChunkOptions
        {
            MaxTokensPerChunk = 25,
            RepeatHeaders = true
        }).ToListAsync();

        // Assert
        Assert.True(chunks.Count > 1, $"Expected multiple chunks, got {chunks.Count}");
        foreach (var chunk in chunks)
        {
            var lines = chunk.Content.Split('\n');
            Assert.True(lines.Length >= 3);
            Assert.Equal("| Full Name | Job Title | Salary |", lines[0]);
            Assert.Equal("| --- | --- | --- |", lines[1]);
            Assert.StartsWith("| ", lines[2]);
        }
    }

    [Fact]
    public async Task ToLlmChunksAsync_CustomTemplate_FormatsCorrectly()
    {
        // Arrange
        var employees = new List<TestEmployee>
        {
            new() { Name = "Alice Smith", Title = "Lead Developer", Salary = 120000.0 },
            new() { Name = "Bob Jones", Title = "Senior Designer", Salary = 95000.0 }
        };

        var source = ToAsyncEnumerable(employees);

        // Act
        var chunks = await source.ToLlmChunksAsync(new LlmChunkOptions
        {
            MaxTokensPerChunk = 1000,
            CustomTemplate = "Employee {Full Name} works as a {Job Title} making {Salary}."
        }).ToListAsync();

        // Assert
        Assert.Single(chunks);
        var chunk = chunks[0];
        var lines = chunk.Content.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("Employee Alice Smith works as a Lead Developer making 120000.", lines[0]);
        Assert.Equal("Employee Bob Jones works as a Senior Designer making 95000.", lines[1]);
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

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }
}
