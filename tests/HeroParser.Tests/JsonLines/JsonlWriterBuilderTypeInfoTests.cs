using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroParser.JsonLines.Writing;
using JsonlApi = HeroParser.Jsonl;
using JsonTypeInfoOf = System.Text.Json.Serialization.Metadata.JsonTypeInfo<HeroParser.Tests.JsonLines.AotPerson>;
using Xunit;

namespace HeroParser.Tests.JsonLines;

/// <summary>A record serialized through a generated context rather than reflection.</summary>
public sealed class AotPerson
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

[JsonSerializable(typeof(AotPerson))]
internal partial class AotPersonContext : JsonSerializerContext;

/// <summary>
/// Covers <see cref="JsonlWriterBuilder{T}"/>'s JsonTypeInfo overloads.
///
/// These exist so JSONL writing works under trimming and Native AOT — they are the only
/// path that does not fall back to reflection — and every one of them was uncovered, so
/// the AOT-safe half of the writer was untested while the reflection half was not.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class JsonlWriterBuilderTypeInfoTests : IDisposable
{
    private readonly List<string> tempFiles = [];

    public void Dispose()
    {
        foreach (string path in tempFiles.Where(File.Exists))
        {
            File.Delete(path);
        }
        GC.SuppressFinalize(this);
    }

    private string TempPath()
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");
        tempFiles.Add(path);
        return path;
    }


    private static readonly AotPerson[] People =
    [
        new() { Name = "Alice", Age = 30 },
        new() { Name = "Bob", Age = 25 },
    ];

    private static JsonTypeInfoOf TypeInfo => (JsonTypeInfoOf)AotPersonContext.Default.GetTypeInfo(typeof(AotPerson))!;

    [Fact]
    public void ToText_SerializesOneRecordPerLine()
    {
        string text = JsonlApi.Write<AotPerson>().ToText(People, TypeInfo);

        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"Alice\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"Bob\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ToText_FromANonCollectionSequence_StillWorks()
    {
        // The builder pre-sizes its buffer from ICollection/IReadOnlyCollection; a lazy
        // sequence takes the default-capacity path instead.
        string text = JsonlApi.Write<AotPerson>().ToText(People.Where(p => p.Age > 0), TypeInfo);
        Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void ToStream_WritesToTheGivenStream()
    {
        using var stream = new MemoryStream();
        JsonlApi.Write<AotPerson>().ToStream(stream, People, TypeInfo, leaveOpen: true);

        Assert.Contains("Alice", Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void ToFile_WritesTheFile()
    {
        string path = TempPath();
        JsonlApi.Write<AotPerson>().ToFile(path, People, TypeInfo);

        Assert.Contains("Bob", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToStreamAsync_WritesAnAsyncSequence()
    {
        using var stream = new MemoryStream();
        await JsonlApi.Write<AotPerson>()
            .ToStreamAsync(stream, AsAsync(People), TypeInfo, leaveOpen: true, TestContext.Current.CancellationToken);

        Assert.Contains("Alice", Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToFileAsync_WritesAnAsyncSequence()
    {
        string path = TempPath();
        await JsonlApi.Write<AotPerson>()
            .ToFileAsync(path, AsAsync(People), TypeInfo, TestContext.Current.CancellationToken);

        Assert.Contains("Bob", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public void WithFinalNewline_AddsATrailingSeparator()
    {
        string without = JsonlApi.Write<AotPerson>().ToText(People, TypeInfo);
        string with = JsonlApi.Write<AotPerson>().WithFinalNewline().ToText(People, TypeInfo);

        Assert.False(without.EndsWith('\n'));
        Assert.True(with.EndsWith('\n'));
    }

    [Fact]
    public void WithNewLine_ChangesTheSeparator()
    {
        string text = JsonlApi.Write<AotPerson>().WithNewLine("\r\n").ToText(People, TypeInfo);
        Assert.Contains("}\r\n{", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNewLine_RejectsAnEmptySeparator()
        => Assert.Throws<ArgumentException>(() => JsonlApi.Write<AotPerson>().WithNewLine(""));

    [Fact]
    public void BuilderMethods_AreFluentAndRejectNulls()
    {
        var builder = JsonlApi.Write<AotPerson>();

        Assert.Same(builder, builder.WithJsonOptions(JsonSerializerOptions.Default));
        Assert.Same(builder, builder.WithTypeInfo(TypeInfo));
        Assert.Same(builder, builder.WithNewLine("\n"));
        Assert.Same(builder, builder.WithEncoding(Encoding.UTF8));
        Assert.Same(builder, builder.WithMaxRowCount(10));
        Assert.Same(builder, builder.WithMaxOutputSize(1024));
        Assert.Same(builder, builder.WithFinalNewline(false));
        Assert.Same(builder, builder.OnError((_, _) => JsonlSerializeErrorAction.Throw));

        Assert.Throws<ArgumentNullException>(() => builder.WithJsonOptions(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithTypeInfo(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithEncoding(null!));
        Assert.Throws<ArgumentNullException>(() => builder.OnError(null!));
    }

    [Fact]
    public void TypeInfoOverloads_RejectNullArguments()
    {
        var builder = JsonlApi.Write<AotPerson>();
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() => builder.ToText(null!, TypeInfo));
        Assert.Throws<ArgumentNullException>(() => builder.ToText(People, null!));
        Assert.Throws<ArgumentNullException>(() => builder.ToFile(null!, People, TypeInfo));
        Assert.Throws<ArgumentNullException>(() => builder.ToFile("x.jsonl", null!, TypeInfo));
        Assert.Throws<ArgumentNullException>(() => builder.ToStream(null!, People, TypeInfo));
        Assert.Throws<ArgumentNullException>(() => builder.ToStream(stream, null!, TypeInfo));
        Assert.Throws<ArgumentNullException>(() => builder.ToStream(stream, People, null!));
    }

    [Fact]
    public async Task AsyncTypeInfoOverloads_RejectNullArguments()
    {
        var builder = JsonlApi.Write<AotPerson>();
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await builder.ToFileAsync(null!, AsAsync(People), TypeInfo, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await builder.ToStreamAsync(null!, AsAsync(People), TypeInfo, true, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await builder.ToStreamAsync(stream, null!, TypeInfo, true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MaxRowCount_StopsAnOverlongRun()
    {
        var builder = JsonlApi.Write<AotPerson>().WithMaxRowCount(1);
        Assert.ThrowsAny<Exception>(() => builder.ToText(People, TypeInfo));
    }

    private static async IAsyncEnumerable<AotPerson> AsAsync(IEnumerable<AotPerson> people)
    {
        foreach (var person in people)
        {
            await Task.Yield();
            yield return person;
        }
    }
}
