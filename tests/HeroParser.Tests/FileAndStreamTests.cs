using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace HeroParser.Tests;

public class FileAndStreamTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void ReadFromFile_ParsesCsvFromDisk()
    {
        var csv = "name,age\nJane,42\nBob,25";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");

        try
        {
            File.WriteAllText(path, csv, Encoding.UTF8);

            using var reader = Csv.ReadFromFile(path);
            Assert.True(reader.MoveNext());
            Assert.Equal("name", reader.Current[0].ToString());
            Assert.Equal("age", reader.Current[1].ToString());

            Assert.True(reader.MoveNext());
            Assert.Equal("Jane", reader.Current[0].ToString());
            Assert.Equal("42", reader.Current[1].ToString());

            Assert.True(reader.MoveNext());
            Assert.Equal("Bob", reader.Current[0].ToString());
            Assert.Equal("25", reader.Current[1].ToString());

            Assert.False(reader.MoveNext());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void ReadFromStream_LeavesStreamOpenByDefault()
    {
        var csv = "a,b\n1,2";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        using var reader = Csv.ReadFromStream(stream);
        Assert.True(reader.MoveNext());
        Assert.Equal("a", reader.Current[0].ToString());
        Assert.Equal("b", reader.Current[1].ToString());
        Assert.True(reader.MoveNext());
        Assert.Equal("1", reader.Current[0].ToString());
        Assert.Equal("2", reader.Current[1].ToString());
        Assert.False(reader.MoveNext());

        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void ReadFromStream_CanCloseStreamWhenRequested()
    {
        var csv = "a,b";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        using var reader = Csv.ReadFromStream(stream, leaveOpen: false);
        Assert.True(reader.MoveNext());
        Assert.Equal("a", reader.Current[0].ToString());
        Assert.Equal("b", reader.Current[1].ToString());
        Assert.False(reader.MoveNext());

        Assert.False(stream.CanRead);
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task ReadFromFileAsync_ParsesCsvFromDisk()
    {
        var csv = "name,age\nJane,42\nBob,25";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv");
        var ct = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllTextAsync(path, csv, Encoding.UTF8, ct);

            var source = await Csv.ReadFromFileAsync(path, cancellationToken: ct);
            using var reader = source.CreateReader();
            Assert.True(reader.MoveNext());
            Assert.Equal("name", reader.Current[0].ToString());
            Assert.Equal("age", reader.Current[1].ToString());

            Assert.True(reader.MoveNext());
            Assert.Equal("Jane", reader.Current[0].ToString());
            Assert.Equal("42", reader.Current[1].ToString());

            Assert.True(reader.MoveNext());
            Assert.Equal("Bob", reader.Current[0].ToString());
            Assert.Equal("25", reader.Current[1].ToString());

            Assert.False(reader.MoveNext());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task ReadFromStreamAsync_LeavesStreamOpenByDefault()
    {
        var csv = "a,b\n1,2";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var ct = TestContext.Current.CancellationToken;

        var source = await Csv.ReadFromStreamAsync(stream, cancellationToken: ct);
        using var reader = source.CreateReader();
        Assert.True(reader.MoveNext());
        Assert.Equal("a", reader.Current[0].ToString());
        Assert.Equal("b", reader.Current[1].ToString());
        Assert.True(reader.MoveNext());
        Assert.Equal("1", reader.Current[0].ToString());
        Assert.Equal("2", reader.Current[1].ToString());
        Assert.False(reader.MoveNext());

        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task ReadFromStreamAsync_CanCloseStreamWhenRequested()
    {
        var csv = "a,b";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var ct = TestContext.Current.CancellationToken;

        var source = await Csv.ReadFromStreamAsync(stream, leaveOpen: false, cancellationToken: ct);
        using var reader = source.CreateReader();
        Assert.True(reader.MoveNext());
        Assert.Equal("a", reader.Current[0].ToString());
        Assert.Equal("b", reader.Current[1].ToString());
        Assert.False(reader.MoveNext());

        Assert.False(stream.CanRead);
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task AsyncStreamReader_ParsesRowsWithoutBufferingAll()
    {
        var rows = Enumerable.Range(0, 2000).Select(i => $"r{i},c{i}");
        var csv = string.Join('\n', rows);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ct = TestContext.Current.CancellationToken;
        await using var reader = Csv.CreateAsyncStreamReader(stream, bufferSize: 32);
        int count = 0;
        while (await reader.MoveNextAsync(ct))
        {
            Assert.Equal($"r{count}", reader.Current[0].ToString());
            Assert.Equal($"c{count}", reader.Current[1].ToString());
            count++;
        }

        Assert.Equal(2000, count);
        Assert.True(stream.CanRead);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task AsyncStreamReader_RespectsLeaveOpenFalse()
    {
        var csv = "a,b\n1,2";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var ct = TestContext.Current.CancellationToken;
        var reader = Csv.CreateAsyncStreamReader(stream, leaveOpen: false);
        await using (reader)
        {
            int count = 0;
            while (await reader.MoveNextAsync(ct))
            {
                count++;
            }
            Assert.Equal(2, count);
        }

        Assert.False(stream.CanRead);
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }
}
