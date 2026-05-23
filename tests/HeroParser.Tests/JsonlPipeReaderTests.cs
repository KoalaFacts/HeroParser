using System.IO.Pipelines;
using System.Text;
using HeroParser.JsonLines.Reading;
using Xunit;

namespace HeroParser.Tests;

public class JsonlPipeReaderTests
{
    public class Person
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadLinesAsync_YieldsLines()
    {
        byte[] data = Encoding.UTF8.GetBytes("{\"Name\":\"Alice\"}\n{\"Name\":\"Bob\"}\n");
        using var stream = new MemoryStream(data);
        PipeReader pipe = PipeReader.Create(stream);

        var lines = new List<string>();
        await foreach (JsonlLine line in Jsonl.ReadLinesAsync(pipe, cancellationToken: TestContext.Current.CancellationToken))
            lines.Add(line.ToString());

        Assert.Equal(2, lines.Count);
        Assert.Contains("Alice", lines[0]);
        Assert.Contains("Bob", lines[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task DeserializeRecordsAsync_StreamsRecords()
    {
        byte[] data = Encoding.UTF8.GetBytes("{\"Name\":\"Alice\",\"Age\":30}\n{\"Name\":\"Bob\",\"Age\":25}\n");
        using var stream = new MemoryStream(data);

        var collected = new List<Person>();
        await foreach (Person p in Jsonl.DeserializeRecordsAsync<Person>(stream, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken))
            collected.Add(p);

        Assert.Equal(2, collected.Count);
        Assert.Equal("Alice", collected[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_HonorsCancellation()
    {
        byte[] data = Encoding.UTF8.GetBytes("{\"Name\":\"Alice\"}\n");
        using var stream = new MemoryStream(data);
        PipeReader pipe = PipeReader.Create(stream);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Jsonl.ReadLinesAsync(pipe, cancellationToken: cts.Token))
            { }
        });
    }
}
