using System.Text.Json.Serialization;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests;

public sealed class AsyncCancelTestRecord
{
    [PositionalMap(Start = 0, Length = 10)]
    public string Name { get; set; } = string.Empty;

    [PositionalMap(Start = 10, Length = 5)]
    public int Value { get; set; }
}

[JsonSerializable(typeof(AsyncCancelTestRecord))]
internal sealed partial class AsyncCancelTestJsonContext : JsonSerializerContext;

public class AsyncFileWriterCancellationTests
{
    private const string SENTINEL = "preserve-existing-file-bytes\n";

    private static readonly AsyncCancelTestRecord[] Records =
    [
        new() { Name = "Alice", Value = 42 }
    ];

    private static async IAsyncEnumerable<AsyncCancelTestRecord> AsyncRecords()
    {
        await Task.Yield();
        yield return Records[0];
    }

    private static async Task AssertPreCanceledPreservesFileAsync(
        Func<string, CancellationToken, Task> writeAction,
        string extension)
    {
        var ct = TestContext.Current.CancellationToken;
        string path = Path.Combine(Path.GetTempPath(), $"heroparser-pre-cancel-{Guid.NewGuid():N}{extension}");
        try
        {
            await File.WriteAllTextAsync(path, SENTINEL, ct);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writeAction(path, cts.Token));
            Assert.Equal(SENTINEL, await File.ReadAllTextAsync(path, ct));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_WriteToFileAsync_PreCanceled_DoesNotTruncateExistingFileAsync()
    {
        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Csv.WriteToFileAsync(path, Records, cancellationToken: token).AsTask(),
            ".csv");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Csv.WriteToFileAsync(path, AsyncRecords(), cancellationToken: token).AsTask(),
            ".csv");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Csv.Write<AsyncCancelTestRecord>().ToFileFromEnumerableAsync(path, Records, token).AsTask(),
            ".csv");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Csv.Write<AsyncCancelTestRecord>().ToFileAsync(path, AsyncRecords(), token).AsTask(),
            ".csv");
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_WriteToFileAsync_PreCanceled_DoesNotTruncateExistingFileAsync()
    {
        await AssertPreCanceledPreservesFileAsync(
            (path, token) => FixedWidth.WriteToFileAsync(path, Records, cancellationToken: token).AsTask(),
            ".txt");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => FixedWidth.WriteToFileAsync(path, AsyncRecords(), cancellationToken: token).AsTask(),
            ".txt");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => FixedWidth.Write<AsyncCancelTestRecord>().ToFileAsync(path, Records, token).AsTask(),
            ".txt");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => FixedWidth.Write<AsyncCancelTestRecord>().ToFileAsync(path, AsyncRecords(), token).AsTask(),
            ".txt");
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Jsonl_WriteToFileAsync_PreCanceled_DoesNotTruncateExistingFileAsync()
    {
        var typeInfo = AsyncCancelTestJsonContext.Default.AsyncCancelTestRecord;

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Jsonl.WriteEnumerableToFileAsync(path, Records, cancellationToken: token).AsTask(),
            ".jsonl");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Jsonl.WriteEnumerableToFileAsync(path, Records, options: null, typeInfo, token).AsTask(),
            ".jsonl");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Jsonl.WriteToFileAsync(path, AsyncRecords(), cancellationToken: token).AsTask(),
            ".jsonl");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Jsonl.WriteToFileAsync(path, AsyncRecords(), options: null, typeInfo, token).AsTask(),
            ".jsonl");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Jsonl.Write<AsyncCancelTestRecord>().ToFileFromEnumerableAsync(path, Records, token).AsTask(),
            ".jsonl");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Jsonl.Write<AsyncCancelTestRecord>().ToFileFromEnumerableAsync(path, Records, typeInfo, token).AsTask(),
            ".jsonl");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Jsonl.Write<AsyncCancelTestRecord>().ToFileAsync(path, AsyncRecords(), token).AsTask(),
            ".jsonl");

        await AssertPreCanceledPreservesFileAsync(
            (path, token) => Jsonl.Write<AsyncCancelTestRecord>().ToFileAsync(path, AsyncRecords(), typeInfo, token).AsTask(),
            ".jsonl");
    }
}
