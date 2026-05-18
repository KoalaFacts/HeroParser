using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers the FixedWidth static-class entry points that lacked direct tests:
/// <c>ReadFromText/CharSpan/ByteSpan/Utf8ByteSpan</c>, <c>ReadFromFile</c>,
/// <c>ReadFromStream</c>, <c>ReadFromFileAsync/StreamAsync</c>,
/// <c>CreateAsyncStreamReader</c>, and the <c>DeserializeRecords</c>/
/// <c>DeserializeRecordsAsync</c> overloads.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class FixedWidthStaticApiTests
{
    [GenerateBinder]
    public sealed class Row
    {
        [PositionalMap(Start = 0, Length = 5)] public string Name { get; set; } = "";
        [PositionalMap(Start = 5, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Age { get; set; }
    }

    private static string SampleText(int n = 3)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.AppendLine($"name{i}{(i + 1):D5}");
        return sb.ToString();
    }

    [Fact]
    public void ReadFromText_ProducesReader()
    {
        var reader = FixedWidth.ReadFromText(SampleText());
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    public void ReadFromCharSpan_ProducesReader()
    {
        var reader = FixedWidth.ReadFromCharSpan(SampleText().AsSpan());
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    public void ReadFromByteSpan_ProducesReader()
    {
        var bytes = Encoding.UTF8.GetBytes(SampleText());
        var reader = FixedWidth.ReadFromByteSpan(bytes);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    public void ReadFromUtf8ByteSpan_ProducesReader()
    {
        var bytes = Encoding.UTF8.GetBytes(SampleText());
        var reader = FixedWidth.ReadFromUtf8ByteSpan(bytes);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    public void ReadFromFile_OpensAndReads()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, SampleText());
            var reader = FixedWidth.ReadFromFile(tmp);
            int n = 0;
            foreach (var _ in reader) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ReadFromStream_OpensAndReads()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SampleText()));
        var reader = FixedWidth.ReadFromStream(ms);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    public async Task ReadFromFileAsync_ProducesSource()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, SampleText(), TestContext.Current.CancellationToken);
            var src = await FixedWidth.ReadFromFileAsync(tmp, cancellationToken: TestContext.Current.CancellationToken);
            int n = 0;
            foreach (var _ in src.CreateReader()) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task ReadFromStreamAsync_ProducesSource()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SampleText()));
        var src = await FixedWidth.ReadFromStreamAsync(ms, cancellationToken: TestContext.Current.CancellationToken);
        int n = 0;
        foreach (var _ in src.CreateReader()) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    public async Task CreateAsyncStreamReader_FromStream()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SampleText()));
        await using var reader = FixedWidth.CreateAsyncStreamReader(ms);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    public async Task CreateAsyncStreamReader_FromFile()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, SampleText(), TestContext.Current.CancellationToken);
            await using var reader = FixedWidth.CreateAsyncStreamReader(tmp);
            int n = 0;
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void DeserializeRecords_FromText()
    {
        var records = FixedWidth.DeserializeRecords<Row>(SampleText());
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public async Task DeserializeRecordsAsync_FromFile()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, SampleText(), TestContext.Current.CancellationToken);
            var list = new List<Row>();
            await foreach (var r in FixedWidth.DeserializeRecordsAsync<Row>(tmp,
                cancellationToken: TestContext.Current.CancellationToken))
            {
                list.Add(r);
            }
            Assert.Equal(3, list.Count);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task DeserializeRecordsAsync_FromStream()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SampleText()));
        var list = new List<Row>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<Row>(ms,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            list.Add(r);
        }
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task DeserializeRecordsAsync_FromPipeReader()
    {
        var bytes = Encoding.UTF8.GetBytes(SampleText());
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);
        var list = new List<Row>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<Row>(pipe,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            list.Add(r);
        }
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void ReadFromText_NullText_Throws()
        => Assert.Throws<ArgumentNullException>(() => FixedWidth.ReadFromText(null!));

    [Fact]
    public void ReadFromFile_NullPath_Throws()
        => Assert.Throws<ArgumentNullException>(() => FixedWidth.ReadFromFile(null!));

    [Fact]
    public void ReadFromStream_NullStream_Throws()
        => Assert.Throws<ArgumentNullException>(() => FixedWidth.ReadFromStream(null!));

    [Fact]
    public async Task ReadFromFileAsync_NullPath_Throws()
    {
        await Task.CompletedTask;
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await FixedWidth.ReadFromFileAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }
}
