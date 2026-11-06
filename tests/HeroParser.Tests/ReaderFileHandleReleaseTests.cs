using HeroParser.Htbs;
using HeroParser.SeparatedValues;
using Xunit;
using HtbApi = HeroParser.Htb;

namespace HeroParser.Tests;

/// <summary>
/// Path-based readers open the file themselves, so when configuration validation fails after the
/// file is open they must close it again. Otherwise the handle stays open until finalization and,
/// on Windows, the file cannot be moved, rewritten or deleted until then. Each test reopens the
/// file with <see cref="FileShare.None"/>, which fails while any handle is still open.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class ReaderFileHandleReleaseTests : IDisposable
{
    private readonly List<string> tempFiles = [];

    public void Dispose()
    {
        foreach (string path in tempFiles.Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    public sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [GenerateBinder]
    public sealed class TypedRow
    {
        [TabularMap(Name = "Type")] public string Type { get; set; } = "";
    }

    [Fact]
    public void MultiSchemaFromFile_WithoutDiscriminator_ThrowsAndClosesTheFile()
    {
        string path = CreateTempFile("Type\nA\n"u8.ToArray());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Csv.Read().WithMultiSchema().MapRecord<TypedRow>("A").FromFile(path));

        Assert.Contains("Discriminator", ex.Message);
        AssertNoOpenHandle(path);
    }

    [Fact]
    public async Task MultiSchemaFromFileAsync_WithoutRecordMappings_ThrowsAndClosesTheFile()
    {
        string path = CreateTempFile("Type\nA\n"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in Csv.Read().WithMultiSchema().WithDiscriminator("Type")
                .FromFileAsync(path, cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        AssertNoOpenHandle(path);
    }

    [Fact]
    public void HtbFromFile_WithInvalidOptions_ThrowsAndClosesTheFile()
    {
        string path = CreateHtbFile();

        Assert.Throws<HtbException>(() => HtbApi.Read<Row>().WithMaxRowCount(0).FromFile(path).ToList());

        AssertNoOpenHandle(path);
    }

    [Fact]
    public async Task HtbFromFileAsync_WithInvalidOptions_ThrowsAndClosesTheFile()
    {
        string path = CreateHtbFile();

        await Assert.ThrowsAsync<HtbException>(async () =>
        {
            await foreach (var _ in HtbApi.Read<Row>().SkipRows(-1).FromFileAsync(path))
            {
            }
        });

        AssertNoOpenHandle(path);
    }

    [Fact]
    public void HtbFromStream_WithInvalidOptionsAndOwnedStream_DisposesTheStream()
    {
        using var stream = new MemoryStream(SerializeHtb());

        Assert.Throws<HtbException>(() =>
            HtbApi.Read<Row>().WithProgress(new Progress<HtbProgress>(), intervalRows: 0)
                .FromStream(stream, leaveOpen: false).ToList());

        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task HtbFromStreamAsync_WithInvalidOptionsAndLeaveOpen_KeepsTheStreamOpen()
    {
        using var stream = new MemoryStream(SerializeHtb());

        await Assert.ThrowsAsync<HtbException>(async () =>
        {
            await foreach (var _ in HtbApi.Read<Row>().WithMaxRowCount(-1).FromStreamAsync(stream, leaveOpen: true))
            {
            }
        });

        Assert.True(stream.CanRead);
    }

    private string CreateHtbFile() => CreateTempFile(SerializeHtb());

    private static byte[] SerializeHtb()
    {
        using var buffer = new MemoryStream();
        HtbApi.Write<Row>().ToStream(buffer, [new Row { Id = 1, Name = "a" }], leaveOpen: true);
        return buffer.ToArray();
    }

    private string CreateTempFile(byte[] content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"heroparser-handle-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(path, content);
        tempFiles.Add(path);
        return path;
    }

    private static void AssertNoOpenHandle(string path)
    {
        // FileShare.None succeeds only when no other handle to the file is open.
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanWrite);
    }
}
