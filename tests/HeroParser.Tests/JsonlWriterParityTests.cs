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

        string serialized = Jsonl.SerializeRecords(items, options: null, typeInfo);
        Assert.Contains("\"Id\":1", serialized);
        Assert.Contains("\"Name\":\"Beta\"", serialized);

        using var syncStream = new MemoryStream();
        Jsonl.WriteToStream(syncStream, items, options: null, typeInfo, leaveOpen: true);
        Assert.True(syncStream.CanRead);
        Assert.Equal(serialized, Encoding.UTF8.GetString(syncStream.ToArray()));

        using var asyncEnumStream = new MemoryStream();
        await Jsonl.WriteEnumerableToStreamAsync(asyncEnumStream, items, options: null, typeInfo, leaveOpen: true, cancellationToken: ct);
        Assert.True(asyncEnumStream.CanRead);
        Assert.Equal(serialized, Encoding.UTF8.GetString(asyncEnumStream.ToArray()));

        using var asyncStreamSeq = new MemoryStream();
        await Jsonl.WriteToStreamAsync(asyncStreamSeq, AsAsyncEnumerable(items), options: null, typeInfo, leaveOpen: true, cancellationToken: ct);
        Assert.True(asyncStreamSeq.CanRead);
        Assert.Equal(serialized, Encoding.UTF8.GetString(asyncStreamSeq.ToArray()));

        using var asyncReflectionStream = new MemoryStream();
        await Jsonl.WriteEnumerableToStreamAsync(asyncReflectionStream, items, new JsonlWriteOptions { WriteFinalNewline = false }, leaveOpen: true, cancellationToken: ct);
        Assert.True(asyncReflectionStream.CanRead);
        Assert.False(Encoding.UTF8.GetString(asyncReflectionStream.ToArray()).EndsWith('\n'));

        string dir = Path.Combine(Path.GetTempPath(), $"hero-jsonl-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string syncPath = Path.Combine(dir, "sync.jsonl.gz");
            Jsonl.WriteToFile(syncPath, items, options: null, typeInfo);

            string asyncEnumPath = Path.Combine(dir, "enum-async.jsonl.br");
            await Jsonl.WriteEnumerableToFileAsync(asyncEnumPath, items, options: null, typeInfo, cancellationToken: ct);

            string asyncSeqPath = Path.Combine(dir, "seq-async.jsonl.gz");
            await Jsonl.WriteToFileAsync(asyncSeqPath, AsAsyncEnumerable(items), options: null, typeInfo, cancellationToken: ct);

            string reflAsyncPath = Path.Combine(dir, "refl-async.jsonl.br");
            await Jsonl.WriteEnumerableToFileAsync(reflAsyncPath, items, cancellationToken: ct);

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
        await Jsonl.Write<JsonlParityItem>().ToStreamFromEnumerableAsync(typedStream, items, typeInfo, leaveOpen: true, cancellationToken: ct);
        Assert.True(typedStream.CanRead);
        Assert.Contains("\"Name\":\"Gamma\"", Encoding.UTF8.GetString(typedStream.ToArray()));

        using var reflStream = new MemoryStream();
        await Jsonl.Write<JsonlParityItem>().ToStreamFromEnumerableAsync(reflStream, items, leaveOpen: true, cancellationToken: ct);
        Assert.True(reflStream.CanRead);
        Assert.Contains("\"Name\":\"Delta\"", Encoding.UTF8.GetString(reflStream.ToArray()));

        string dir = Path.Combine(Path.GetTempPath(), $"hero-jsonl-builder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string typedPath = Path.Combine(dir, "builder-typed.jsonl.br");
            await Jsonl.Write<JsonlParityItem>().ToFileFromEnumerableAsync(typedPath, items, typeInfo, ct);

            string reflPath = Path.Combine(dir, "builder-refl.jsonl.gz");
            await Jsonl.Write<JsonlParityItem>().ToFileFromEnumerableAsync(reflPath, items, ct);

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

    [Fact]
    public async Task Jsonl_StaticFacade_NullOptionsCalls_RemainUnambiguous()
    {
        var ct = TestContext.Current.CancellationToken;
        List<JsonlParityItem> items =
        [
            new() { Id = 7, Name = "NullOptions" }
        ];

        string serialized = Jsonl.SerializeRecords(items, null);
        Assert.Contains("\"Name\":\"NullOptions\"", serialized);

        using (var syncStream = new MemoryStream())
        {
            Jsonl.WriteToStream(syncStream, items, null);
            Assert.Equal(serialized, Encoding.UTF8.GetString(syncStream.ToArray()));
        }

        await using (var asyncEnumStream = new MemoryStream())
        {
            await Jsonl.WriteEnumerableToStreamAsync(asyncEnumStream, items, null, cancellationToken: ct);
            Assert.Equal(serialized, Encoding.UTF8.GetString(asyncEnumStream.ToArray()));
        }

        await using (var asyncSeqStream = new MemoryStream())
        {
            await Jsonl.WriteToStreamAsync(asyncSeqStream, AsAsyncEnumerable(items), null, cancellationToken: ct);
            Assert.Equal(serialized, Encoding.UTF8.GetString(asyncSeqStream.ToArray()));
        }

        string dir = Path.Combine(Path.GetTempPath(), $"hero-jsonl-nullopts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string syncFile = Path.Combine(dir, "sync.jsonl");
            string asyncEnumFile = Path.Combine(dir, "enum.jsonl");
            string asyncSeqFile = Path.Combine(dir, "seq.jsonl");

            Jsonl.WriteToFile(syncFile, items, null);
            await Jsonl.WriteEnumerableToFileAsync(asyncEnumFile, items, null, ct);
            await Jsonl.WriteToFileAsync(asyncSeqFile, AsAsyncEnumerable(items), null, ct);

            Assert.Equal(serialized, await File.ReadAllTextAsync(syncFile, ct));
            Assert.Equal(serialized, await File.ReadAllTextAsync(asyncEnumFile, ct));
            Assert.Equal(serialized, await File.ReadAllTextAsync(asyncSeqFile, ct));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AsyncWriters_DualEnumerableAndAsyncEnumerableCollection_BindsUnambiguouslyAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var dual = new DualCollection<JsonlParityItem>(
        [
            new() { Id = 1, Name = "DualOne" }
        ]);
        var typeInfo = JsonlParityContext.Default.JsonlParityItem;

        string dir = Path.Combine(Path.GetTempPath(), $"hero-dual-async-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            await using var jsonlStream = new MemoryStream();
            await Jsonl.WriteToStreamAsync(jsonlStream, dual, cancellationToken: ct);
            Assert.Contains("\"Name\":\"DualOne\"", Encoding.UTF8.GetString(jsonlStream.ToArray()));

            await using var builderJsonlStream = new MemoryStream();
            await Jsonl.Write<JsonlParityItem>().ToStreamAsync(builderJsonlStream, dual, leaveOpen: true, cancellationToken: ct);
            Assert.Contains("\"Name\":\"DualOne\"", Encoding.UTF8.GetString(builderJsonlStream.ToArray()));

            await using var typedJsonlStream = new MemoryStream();
            await Jsonl.Write<JsonlParityItem>().ToStreamAsync(typedJsonlStream, dual, typeInfo, leaveOpen: true, cancellationToken: ct);
            Assert.Contains("\"Name\":\"DualOne\"", Encoding.UTF8.GetString(typedJsonlStream.ToArray()));

            await using var csvStream = new MemoryStream();
            await Csv.Write<JsonlParityItem>().ToStreamAsync(csvStream, dual, leaveOpen: true, cancellationToken: ct);
            Assert.Contains("DualOne", Encoding.UTF8.GetString(csvStream.ToArray()));

            string jsonlFile = Path.Combine(dir, "facade.jsonl");
            string jsonlBuilderFile = Path.Combine(dir, "builder.jsonl");
            string csvFile = Path.Combine(dir, "builder.csv");

            await Jsonl.WriteToFileAsync(jsonlFile, dual, cancellationToken: ct);
            await Jsonl.Write<JsonlParityItem>().ToFileAsync(jsonlBuilderFile, dual, ct);
            await Csv.Write<JsonlParityItem>().ToFileAsync(csvFile, dual, ct);

            Assert.Contains("\"Name\":\"DualOne\"", await File.ReadAllTextAsync(jsonlFile, ct));
            Assert.Contains("\"Name\":\"DualOne\"", await File.ReadAllTextAsync(jsonlBuilderFile, ct));
            Assert.Contains("DualOne", await File.ReadAllTextAsync(csvFile, ct));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class DualCollection<T>(IReadOnlyList<T> items) : IEnumerable<T>, IAsyncEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            foreach (T item in items)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }
}
