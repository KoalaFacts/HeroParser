using HeroParser.Cli;
using HeroParser.Tests.ConsoleUi;
using System.Text;
using Xunit;

namespace HeroParser.Tests.Cli;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class CsvRowBatchSourceTests : IDisposable
{
    private readonly List<string> paths = [];

    public void Dispose()
    {
        foreach (string path in paths)
            File.Delete(path);
    }

    private string FileWith(string content)
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, content);
        paths.Add(path);
        return path;
    }

    [Fact]
    public async Task FirstBatch_DoesNotParseOversizedTail()
    {
        string path = FileWith("Name,Age\nAlice,30\nBob,25\n" + new string('x', 600_000));
        await using var source = await CsvRowBatchSource.OpenAsync(path, ',');

        var batch = await source.ReadBatchAsync(2);

        Assert.Equal(["Name", "Age"], source.Headers);
        Assert.Equal(2, batch.Count);
        Assert.Equal(["Alice", "30"], batch[0]);
        Assert.Equal(["Bob", "25"], batch[1]);
    }

    [Fact]
    public async Task CountAndBatches_AgreeOnLogicalQuotedRows()
    {
        string path = FileWith("Name,Note\nAlice,\"first\nsecond\"\nBob,last\n");
        await using var counting = await CsvRowBatchSource.OpenAsync(path, ',');
        Assert.Equal(2, await counting.CountRemainingRowsAsync());

        await using var reading = await CsvRowBatchSource.OpenAsync(path, ',');
        Assert.Single(await reading.ReadBatchAsync(1));
        Assert.Single(await reading.ReadBatchAsync(1));
        Assert.Empty(await reading.ReadBatchAsync(1));
    }

    [Fact]
    public async Task CountAndBatches_AcceptRowsOverDefaultLimit()
    {
        string path = FileWith("Value\n" + new string('x', 600_000));
        await using var counting = await CsvRowBatchSource.OpenAsync(path, ',');
        Assert.Equal(1, await counting.CountRemainingRowsAsync());

        await using var reading = await CsvRowBatchSource.OpenAsync(path, ',');
        var batch = await reading.ReadBatchAsync(1);
        Assert.Equal(600_000, Assert.Single(batch)[0].Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utf16_CountAndBatches_PreserveQuotedRowsAndUnicode(bool bigEndian)
    {
        string path = FileWith("");
        File.WriteAllText(path, "Name,Note\r\n猫,\"line\n😀\"\r\n犬,last\r\n",
            bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode);

        await using var counting = await CsvRowBatchSource.OpenAsync(path, ',');
        Assert.Equal(2, await counting.CountRemainingRowsAsync());

        await using var reading = await CsvRowBatchSource.OpenAsync(path, ',');
        Assert.Equal(["Name", "Note"], reading.Headers);
        Assert.Equal(["猫", "\"line\n😀\""], Assert.Single(await reading.ReadBatchAsync(1)));
        Assert.Equal(["犬", "last"], Assert.Single(await reading.ReadBatchAsync(1)));
        Assert.Empty(await reading.ReadBatchAsync(1));
    }
}
