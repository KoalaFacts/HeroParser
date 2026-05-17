using HeroParser.Streaming;
using Xunit;

namespace HeroParser.Tests;

public class BatchAsyncTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ExactMultiple_YieldsFullBatches()
    {
        IAsyncEnumerable<int> source = ToAsync(Enumerable.Range(0, 10));

        var batches = new List<IReadOnlyList<int>>();
        await foreach (var batch in source.BatchAsync(5, TestContext.Current.CancellationToken))
            batches.Add(batch);

        Assert.Equal(2, batches.Count);
        Assert.Equal(5, batches[0].Count);
        Assert.Equal(5, batches[1].Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Remainder_YieldsPartialFinalBatch()
    {
        IAsyncEnumerable<int> source = ToAsync(Enumerable.Range(0, 7));

        var batches = new List<IReadOnlyList<int>>();
        await foreach (var batch in source.BatchAsync(3, TestContext.Current.CancellationToken))
            batches.Add(batch);

        Assert.Equal(3, batches.Count);
        Assert.Equal(3, batches[0].Count);
        Assert.Equal(3, batches[1].Count);
        Assert.Single(batches[2]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task EmptySource_YieldsNoBatches()
    {
        IAsyncEnumerable<int> source = ToAsync(Enumerable.Empty<int>());

        var batches = new List<IReadOnlyList<int>>();
        await foreach (var batch in source.BatchAsync(10, TestContext.Current.CancellationToken))
            batches.Add(batch);

        Assert.Empty(batches);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ZeroSize_Throws()
    {
        IAsyncEnumerable<int> source = ToAsync(Enumerable.Range(0, 5));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in source.BatchAsync(0, TestContext.Current.CancellationToken))
            { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PreservesOrder()
    {
        IAsyncEnumerable<int> source = ToAsync(Enumerable.Range(0, 6));

        var flat = new List<int>();
        await foreach (var batch in source.BatchAsync(2, TestContext.Current.CancellationToken))
            flat.AddRange(batch);

        Assert.Equal([0, 1, 2, 3, 4, 5], flat);
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> source)
    {
        foreach (T item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
