using System.Text;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers every entry point on the <see cref="Csv"/> write gateway.
///
/// These overloads are the API most callers actually touch, and several of them —
/// the file variants, the async ones, and the factory methods that hand back a writer —
/// had never been called. Each is a thin wrapper, which is exactly why a swapped
/// argument or a missing Validate() in one of them would go unnoticed.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class CsvWriteGatewayTests : IDisposable
{
    public sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    private static readonly Person[] People =
    [
        new() { Name = "Alice", Age = 30 },
        new() { Name = "Bob", Age = 25 },
    ];

    private readonly List<string> tempFiles = [];

    public void Dispose()
    {
        foreach (string path in tempFiles.Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    private string TempPath()
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        tempFiles.Add(path);
        return path;
    }

    private static async IAsyncEnumerable<Person> AsAsync(IEnumerable<Person> people)
    {
        foreach (var person in people)
        {
            await Task.Yield();
            yield return person;
        }
    }

    private static void AssertHasBothRows(string csv)
    {
        Assert.Contains("Name,Age", csv, StringComparison.Ordinal);
        Assert.Contains("Alice,30", csv, StringComparison.Ordinal);
        Assert.Contains("Bob,25", csv, StringComparison.Ordinal);
    }

    // ---- writer factories ------------------------------------------------------

    [Fact]
    public void CreateWriter_WritesThroughTheGivenTextWriter()
    {
        using var text = new StringWriter();
        using (var writer = Csv.CreateWriter(text, leaveOpen: true))
        {
            writer.WriteField("a");
            writer.EndRow();
        }

        Assert.Contains("a", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWriter_NullTextWriter_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.CreateWriter(null!));

    [Fact]
    public void CreateFileWriter_WritesTheFile()
    {
        string path = TempPath();
        using (var writer = Csv.CreateFileWriter(path))
        {
            writer.WriteField("value");
            writer.EndRow();
        }

        Assert.Contains("value", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateFileWriter_WithoutAPath_Throws(string? path)
        => Assert.ThrowsAny<ArgumentException>(() => Csv.CreateFileWriter(path!));

    [Fact]
    public void CreateStreamWriter_LeavesTheStreamOpenWhenAsked()
    {
        using var stream = new MemoryStream();
        using (var writer = Csv.CreateStreamWriter(stream, leaveOpen: true))
        {
            writer.WriteField("kept");
            writer.EndRow();
        }

        // Disposing the writer must not close a stream the caller still owns.
        Assert.True(stream.CanWrite, "the stream should still be usable");
        Assert.Contains("kept", Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateStreamWriter_ClosesTheStreamByRequest()
    {
        var stream = new MemoryStream();
        using (var writer = Csv.CreateStreamWriter(stream, leaveOpen: false))
        {
            writer.WriteField("gone");
            writer.EndRow();
        }

        Assert.False(stream.CanWrite, "the stream should have been closed with the writer");
    }

    [Fact]
    public void CreateStreamWriter_NullStream_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.CreateStreamWriter(null!));

    [Fact]
    public async Task CreateAsyncStreamWriter_WritesThroughTheStream()
    {
        var ct = TestContext.Current.CancellationToken;
        using var stream = new MemoryStream();
        await using (var writer = Csv.CreateAsyncStreamWriter(stream, leaveOpen: true))
        {
            await writer.WriteFieldAsync("async", ct);
            await writer.EndRowAsync(ct);
        }

        Assert.Contains("async", Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAsyncStreamWriter_NullStream_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.CreateAsyncStreamWriter(null!));

    // ---- record writing --------------------------------------------------------

    [Fact]
    public void WriteToText_SerializesRecords() => AssertHasBothRows(Csv.WriteToText(People));

    [Fact]
    public void WriteToText_NullRecords_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.WriteToText<Person>(null!));

    [Fact]
    public void SerializeRecords_MatchesWriteToText()
        => Assert.Equal(Csv.WriteToText(People), Csv.SerializeRecords(People));

    [Fact]
    public void WriteToText_HeaderCanBeSuppressed()
    {
        string csv = Csv.WriteToText(People, new CsvWriteOptions { WriteHeader = false });

        Assert.DoesNotContain("Name,Age", csv, StringComparison.Ordinal);
        Assert.Contains("Alice,30", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteToStream_SerializesRecords()
    {
        using var stream = new MemoryStream();
        Csv.WriteToStream(stream, People, leaveOpen: true);

        AssertHasBothRows(Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void WriteToStream_NullArguments_Throw()
    {
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => Csv.WriteToStream(null!, People));
        Assert.Throws<ArgumentNullException>(() => Csv.WriteToStream<Person>(stream, null!));
    }

    [Fact]
    public void WriteToFile_SerializesRecords()
    {
        string path = TempPath();
        Csv.WriteToFile(path, People);

        AssertHasBothRows(File.ReadAllText(path));
    }

    [Fact]
    public void WriteToFile_NullArguments_Throw()
    {
        Assert.ThrowsAny<ArgumentException>(() => Csv.WriteToFile("", People));
        Assert.Throws<ArgumentNullException>(() => Csv.WriteToFile<Person>(TempPath(), null!));
    }

    // ---- async record writing --------------------------------------------------

    [Fact]
    public async Task WriteToTextAsync_SerializesAnAsyncSequence()
        => AssertHasBothRows(await Csv.WriteToTextAsync(AsAsync(People), cancellationToken: TestContext.Current.CancellationToken));

    [Fact]
    public async Task WriteToTextAsync_NullRecords_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Csv.WriteToTextAsync<Person>(null!, cancellationToken: TestContext.Current.CancellationToken));

    [Fact]
    public async Task WriteToStreamAsync_SerializesAnAsyncSequence()
    {
        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(stream, AsAsync(People), cancellationToken: TestContext.Current.CancellationToken);

        AssertHasBothRows(Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task WriteToStreamAsync_SerializesASynchronousSequence()
    {
        // The IEnumerable overload exists to skip the async-iterator machinery for
        // in-memory collections; it has to produce the same bytes.
        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(stream, People, cancellationToken: TestContext.Current.CancellationToken);

        AssertHasBothRows(Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task WriteToStreamAsync_NullArguments_Throw()
    {
        var ct = TestContext.Current.CancellationToken;
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Csv.WriteToStreamAsync(null!, AsAsync(People), cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Csv.WriteToStreamAsync<Person>(stream, (IAsyncEnumerable<Person>)null!, cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Csv.WriteToStreamAsync<Person>(stream, (IEnumerable<Person>)null!, cancellationToken: ct));
    }

    [Fact]
    public async Task WriteToFileAsync_SerializesAnAsyncSequence()
    {
        string path = TempPath();
        await Csv.WriteToFileAsync(path, AsAsync(People), cancellationToken: TestContext.Current.CancellationToken);

        AssertHasBothRows(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteToFileAsync_SerializesASynchronousSequence()
    {
        string path = TempPath();
        await Csv.WriteToFileAsync(path, People, cancellationToken: TestContext.Current.CancellationToken);

        AssertHasBothRows(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteToFileAsync_NullArguments_Throw()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await Csv.WriteToFileAsync("", AsAsync(People), cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Csv.WriteToFileAsync(TempPath(), (IAsyncEnumerable<Person>)null!, cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Csv.WriteToFileAsync(TempPath(), (IEnumerable<Person>)null!, cancellationToken: ct));
    }

    [Fact]
    public void InvalidOptions_AreRejectedBeforeAnythingIsWritten()
    {
        // Validate() runs on the way in, so a bad option set never produces a half file.
        var options = new CsvWriteOptions { Delimiter = '"', Quote = '"' };
        Assert.ThrowsAny<Exception>(() => Csv.WriteToText(People, options));
    }
}
