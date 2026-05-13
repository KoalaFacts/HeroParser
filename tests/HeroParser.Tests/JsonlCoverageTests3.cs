using System.IO.Pipelines;
using System.Text;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using HeroParser.JsonLines.Writing;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Third batch — pushes coverage on builder typed async terminals, OnError async paths,
/// line-reader buffer growth, and remaining edge branches.
/// </summary>
public class JsonlCoverageTests3
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_TypedAsync_FromFileAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hero-jsonl-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, "{\"Name\":\"A\",\"Age\":1}\n{\"Name\":\"B\",\"Age\":2}\n", TestContext.Current.CancellationToken);
        try
        {
            List<JsonlCoveragePerson> collected = [];
            await foreach (JsonlCoveragePerson p in Jsonl.Read<JsonlCoveragePerson>()
                .WithTypeInfo(JsonlCoveragePersonContext.Default.JsonlCoveragePerson)
                .FromFileAsync(path, TestContext.Current.CancellationToken))
            {
                collected.Add(p);
            }
            Assert.Equal(2, collected.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_TypedAsync_FromStreamAsync()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("{\"Name\":\"A\",\"Age\":1}\n");
        using var stream = new MemoryStream(utf8);
        List<JsonlCoveragePerson> collected = [];
        await foreach (JsonlCoveragePerson p in Jsonl.Read<JsonlCoveragePerson>()
            .WithTypeInfo(JsonlCoveragePersonContext.Default.JsonlCoveragePerson)
            .FromStreamAsync(stream, leaveOpen: true, TestContext.Current.CancellationToken))
        {
            collected.Add(p);
        }
        Assert.Single(collected);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_TypedAsync_FromPipeReader()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("{\"Name\":\"A\",\"Age\":1}\n");
        using var stream = new MemoryStream(utf8);
        List<JsonlCoveragePerson> collected = [];
        await foreach (JsonlCoveragePerson p in Jsonl.Read<JsonlCoveragePerson>()
            .WithTypeInfo(JsonlCoveragePersonContext.Default.JsonlCoveragePerson)
            .FromPipeReaderAsync(PipeReader.Create(stream), TestContext.Current.CancellationToken))
        {
            collected.Add(p);
        }
        Assert.Single(collected);
    }

    // ----- Builder async OnError paths (both typed + reflection) -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_Async_OnError_TypedPath_Skip()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("{\"Name\":\"A\",\"Age\":1}\nBROKEN\n{\"Name\":\"B\",\"Age\":2}\n");
        using var stream = new MemoryStream(utf8);
        int errors = 0;

        List<JsonlCoveragePerson> collected = [];
        await foreach (JsonlCoveragePerson p in Jsonl.Read<JsonlCoveragePerson>()
            .WithTypeInfo(JsonlCoveragePersonContext.Default.JsonlCoveragePerson)
            .OnError((_, _) => { errors++; return JsonlDeserializeErrorAction.SkipRecord; })
            .FromStreamAsync(stream, leaveOpen: true, TestContext.Current.CancellationToken))
        {
            collected.Add(p);
        }

        Assert.Equal(1, errors);
        Assert.Equal(2, collected.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_Async_OnError_ReflectionPath_Throw()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("BROKEN\n");
        using var stream = new MemoryStream(utf8);

        await Assert.ThrowsAsync<JsonlException>(async () =>
        {
            await foreach (var _ in Jsonl.Read<JsonlCoveragePerson>()
                .OnError((_, _) => JsonlDeserializeErrorAction.Throw)
                .FromStreamAsync(stream, leaveOpen: true, TestContext.Current.CancellationToken))
            { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_Async_NoOnError_PropagatesFailure()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("BROKEN\n");
        using var stream = new MemoryStream(utf8);

        await Assert.ThrowsAsync<JsonlException>(async () =>
        {
            await foreach (var _ in Jsonl.Read<JsonlCoveragePerson>()
                .FromStreamAsync(stream, leaveOpen: true, TestContext.Current.CancellationToken))
            { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_Async_MaxRowCount_Throws()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(string.Join('\n',
            Enumerable.Range(0, 5).Select(i => $"{{\"Name\":\"P{i}\",\"Age\":{i}}}")));
        using var stream = new MemoryStream(utf8);

        await Assert.ThrowsAsync<JsonlException>(async () =>
        {
            await foreach (var _ in Jsonl.Read<JsonlCoveragePerson>()
                .WithMaxRowCount(2)
                .FromStreamAsync(stream, leaveOpen: true, TestContext.Current.CancellationToken))
            { }
        });
    }

    // ----- Static facade reflection async paths -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Static_DeserializeRecordsAsync_Reflection_HandlesError()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("BROKEN\n");
        using var stream = new MemoryStream(utf8);
        var options = new JsonlReadOptions { OnError = (_, _) => JsonlDeserializeErrorAction.SkipRecord };

        int yielded = 0;
        await foreach (var _ in Jsonl.DeserializeRecordsAsync<JsonlCoveragePerson>(stream, options, leaveOpen: true, TestContext.Current.CancellationToken))
        {
            yielded++;
        }
        Assert.Equal(0, yielded);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Static_DeserializeRecordsAsync_Typed_HandlesError()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("BROKEN\n");
        using var stream = new MemoryStream(utf8);
        var options = new JsonlReadOptions { OnError = (_, _) => JsonlDeserializeErrorAction.SkipRecord };

        int yielded = 0;
        await foreach (var _ in Jsonl.DeserializeRecordsAsync(
            stream, JsonlCoveragePersonContext.Default.JsonlCoveragePerson, options, leaveOpen: true, TestContext.Current.CancellationToken))
        {
            yielded++;
        }
        Assert.Equal(0, yielded);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Static_DeserializeRecordsAsync_Reflection_MaxRowCount_Throws()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(string.Join('\n',
            Enumerable.Range(0, 5).Select(i => $"{{\"Name\":\"P{i}\",\"Age\":{i}}}")));
        using var stream = new MemoryStream(utf8);
        var options = new JsonlReadOptions { MaxRowCount = 2 };

        await Assert.ThrowsAsync<JsonlException>(async () =>
        {
            await foreach (var _ in Jsonl.DeserializeRecordsAsync<JsonlCoveragePerson>(
                stream, options, leaveOpen: true, TestContext.Current.CancellationToken))
            { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Static_DeserializeRecordsAsync_Reflection_NoOnError_Propagates()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("BROKEN\n");
        using var stream = new MemoryStream(utf8);

        await Assert.ThrowsAsync<JsonlException>(async () =>
        {
            await foreach (var _ in Jsonl.DeserializeRecordsAsync<JsonlCoveragePerson>(
                stream, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken))
            { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Static_DeserializeRecordsAsync_Typed_NoOnError_Propagates()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("BROKEN\n");
        using var stream = new MemoryStream(utf8);

        await Assert.ThrowsAsync<JsonlException>(async () =>
        {
            await foreach (var _ in Jsonl.DeserializeRecordsAsync(
                stream, JsonlCoveragePersonContext.Default.JsonlCoveragePerson, cancellationToken: TestContext.Current.CancellationToken))
            { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Static_DeserializeRecordsAsync_SkipRows()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(string.Join('\n',
            Enumerable.Range(0, 5).Select(i => $"{{\"Name\":\"P{i}\",\"Age\":{i}}}")));
        using var stream = new MemoryStream(utf8);
        var options = new JsonlReadOptions { SkipRows = 3 };

        int yielded = 0;
        await foreach (var _ in Jsonl.DeserializeRecordsAsync<JsonlCoveragePerson>(stream, options, leaveOpen: true, TestContext.Current.CancellationToken))
            yielded++;
        Assert.Equal(2, yielded);
    }

    // ----- JsonlLineReader: buffer growth past initial 8 KiB -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void LineReader_GrowsBufferForLongLine()
    {
        // Force the internal 8 KiB buffer to grow at least once.
        string longContent = new('a', 16 * 1024);
        string jsonl = $"{{\"Name\":\"{longContent}\",\"Age\":1}}";

        using var reader = Jsonl.Read<JsonlCoveragePerson>()
            .WithMaxLineSize(64 * 1024)
            .FromText(jsonl);
        List<JsonlCoveragePerson> people = [.. reader];
        Assert.Single(people);
        Assert.Equal(longContent, people[0].Name);
    }

    // ----- JsonlStreamWriter: async OnError -----

    public class AsyncUnwritable
    {
        public string Boom => throw new InvalidOperationException("nope");
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Writer_Async_OnError_SkipRecord()
    {
        using var stream = new MemoryStream();
        int errors = 0;

        await Jsonl.Write<AsyncUnwritable>()
            .OnError((_, _) => { errors++; return JsonlSerializeErrorAction.SkipRecord; })
            .ToStreamAsync(stream, ToAsync([new AsyncUnwritable()]), leaveOpen: true, TestContext.Current.CancellationToken);

        Assert.Equal(1, errors);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Writer_Async_NoOnError_Throws()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<JsonlException>(async () =>
            await Jsonl.Write<AsyncUnwritable>()
                .ToStreamAsync(stream, ToAsync([new AsyncUnwritable()]), leaveOpen: true, TestContext.Current.CancellationToken));
    }

    // ----- Builder Options that previously weren't asserted past the fluent return -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Jsonl.Read<JsonlCoveragePerson>().WithJsonOptions(null!));
        Assert.Throws<ArgumentNullException>(() => Jsonl.Read<JsonlCoveragePerson>().WithTypeInfo(null!));
        Assert.Throws<ArgumentNullException>(() => Jsonl.Read<JsonlCoveragePerson>().WithProgress(null!));
        Assert.Throws<ArgumentNullException>(() => Jsonl.Read<JsonlCoveragePerson>().OnError(null!));

        Assert.Throws<ArgumentNullException>(() => Jsonl.Write<JsonlCoveragePerson>().WithJsonOptions(null!));
        Assert.Throws<ArgumentNullException>(() => Jsonl.Write<JsonlCoveragePerson>().WithTypeInfo(null!));
        Assert.Throws<ArgumentNullException>(() => Jsonl.Write<JsonlCoveragePerson>().WithEncoding(null!));
        Assert.Throws<ArgumentNullException>(() => Jsonl.Write<JsonlCoveragePerson>().OnError(null!));
        Assert.Throws<ArgumentException>(() => Jsonl.Write<JsonlCoveragePerson>().WithNewLine(string.Empty));
    }

    // ----- Pipe line reader: oversized line spanning multiple pipe segments at EOF -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Pipe_OversizedLineAtEof_Throws()
    {
        // Long line, no terminating newline → exercises the EOF branch with size enforcement.
        byte[] data = Encoding.UTF8.GetBytes(new string('x', 10));
        using var stream = new MemoryStream(data);

        await Assert.ThrowsAsync<JsonlException>(async () =>
        {
            await foreach (var _ in Jsonl.ReadLinesAsync(
                PipeReader.Create(stream),
                new JsonlReadOptions { MaxLineSizeBytes = 5 },
                cancellationToken: TestContext.Current.CancellationToken))
            { }
        });
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
