using System.Text;
using System.Text.Json.Serialization;
using HeroParser.JsonLines.Writing;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public partial class JsonlWriterParityTests
{
    public sealed class JsonlParityItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    [JsonSerializable(typeof(JsonlParityItem))]
    internal partial class JsonlParityContext : JsonSerializerContext;

    private static async IAsyncEnumerable<JsonlParityItem> AsAsyncEnumerable(IEnumerable<JsonlParityItem> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    [Fact]
    public async Task Jsonl_StaticFacade_TypeInfoAndIEnumerableAsync_Overloads_Work()
    {
        var ct = TestContext.Current.CancellationToken;
        List<JsonlParityItem> items =
        [
            new() { Id = 1, Name = "Alpha" },
            new() { Id = 2, Name = "Beta" }
        ];
        var typeInfo = JsonlParityContext.Default.JsonlParityItem;

        string serialized = Jsonl.SerializeRecords(items, typeInfo);
        Assert.Contains("\"Id\":1", serialized);
        Assert.Contains("\"Name\":\"Beta\"", serialized);

        using var syncStream = new MemoryStream();
        Jsonl.WriteToStream(syncStream, items, typeInfo, leaveOpen: true);
        Assert.True(syncStream.CanRead);
        Assert.Equal(serialized, Encoding.UTF8.GetString(syncStream.ToArray()));

        using var asyncEnumStream = new MemoryStream();
        await Jsonl.WriteToStreamAsync(asyncEnumStream, items, typeInfo, leaveOpen: true, cancellationToken: ct);
        Assert.True(asyncEnumStream.CanRead);
        Assert.Equal(serialized, Encoding.UTF8.GetString(asyncEnumStream.ToArray()));

        using var asyncStreamSeq = new MemoryStream();
        await Jsonl.WriteToStreamAsync(asyncStreamSeq, AsAsyncEnumerable(items), typeInfo, leaveOpen: true, cancellationToken: ct);
        Assert.True(asyncStreamSeq.CanRead);
        Assert.Equal(serialized, Encoding.UTF8.GetString(asyncStreamSeq.ToArray()));

        using var asyncReflectionStream = new MemoryStream();
        await Jsonl.WriteToStreamAsync(asyncReflectionStream, items, new JsonlWriteOptions { WriteFinalNewline = false }, leaveOpen: true, cancellationToken: ct);
        Assert.True(asyncReflectionStream.CanRead);
        Assert.False(Encoding.UTF8.GetString(asyncReflectionStream.ToArray()).EndsWith('\n'));

        string dir = Path.Combine(Path.GetTempPath(), $"hero-jsonl-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string syncPath = Path.Combine(dir, "sync.jsonl.gz");
            Jsonl.WriteToFile(syncPath, items, typeInfo);

            string asyncEnumPath = Path.Combine(dir, "enum-async.jsonl.br");
            await Jsonl.WriteToFileAsync(asyncEnumPath, items, typeInfo, cancellationToken: ct);

            string asyncSeqPath = Path.Combine(dir, "seq-async.jsonl.gz");
            await Jsonl.WriteToFileAsync(asyncSeqPath, AsAsyncEnumerable(items), typeInfo, cancellationToken: ct);

            string reflAsyncPath = Path.Combine(dir, "refl-async.jsonl.br");
            await Jsonl.WriteToFileAsync(reflAsyncPath, items, cancellationToken: ct);

            foreach (string path in (string[])[syncPath, asyncEnumPath, asyncSeqPath, reflAsyncPath])
            {
                using var reader = Jsonl.Read<JsonlParityItem>().FromFile(path, typeInfo);
                var readBack = reader.ToList();
                Assert.Equal(2, readBack.Count);
                Assert.Equal("Alpha", readBack[0].Name);
                Assert.Equal(2, readBack[1].Id);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Jsonl_WriterBuilder_IEnumerableAsync_Overloads_Work()
    {
        var ct = TestContext.Current.CancellationToken;
        List<JsonlParityItem> items =
        [
            new() { Id = 10, Name = "Gamma" },
            new() { Id = 20, Name = "Delta" }
        ];
        var typeInfo = JsonlParityContext.Default.JsonlParityItem;

        using var typedStream = new MemoryStream();
        await Jsonl.Write<JsonlParityItem>().ToStreamAsync(typedStream, items, typeInfo, leaveOpen: true, cancellationToken: ct);
        Assert.True(typedStream.CanRead);
        Assert.Contains("\"Name\":\"Gamma\"", Encoding.UTF8.GetString(typedStream.ToArray()));

        using var reflStream = new MemoryStream();
        await Jsonl.Write<JsonlParityItem>().ToStreamAsync(reflStream, items, leaveOpen: true, cancellationToken: ct);
        Assert.True(reflStream.CanRead);
        Assert.Contains("\"Name\":\"Delta\"", Encoding.UTF8.GetString(reflStream.ToArray()));

        string dir = Path.Combine(Path.GetTempPath(), $"hero-jsonl-builder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string typedPath = Path.Combine(dir, "builder-typed.jsonl.br");
            await Jsonl.Write<JsonlParityItem>().ToFileAsync(typedPath, items, typeInfo, ct);

            string reflPath = Path.Combine(dir, "builder-refl.jsonl.gz");
            await Jsonl.Write<JsonlParityItem>().ToFileAsync(reflPath, items, ct);

            foreach (string path in (string[])[typedPath, reflPath])
            {
                using var reader = Jsonl.Read<JsonlParityItem>().FromFile(path, typeInfo);
                var readBack = reader.ToList();
                Assert.Equal(2, readBack.Count);
                Assert.Equal(10, readBack[0].Id);
                Assert.Equal("Delta", readBack[1].Name);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
