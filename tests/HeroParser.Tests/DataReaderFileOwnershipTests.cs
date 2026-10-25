using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Reading.Data;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Data;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class DataReaderFileOwnershipTests
{
    [Fact]
    public void Csv_ReaderConstructionFailure_ReleasesFileHandle()
    {
        string path = CreateTempFile(".csv", "Name\nAlice\n");
        try
        {
            // The parser options pass validation; CsvDataReader itself rejects this
            // schema after CsvAsyncStreamReader has already been constructed.
            var invalidSchema = new CsvDataReaderOptions { ColumnNames = [null!] };

            Assert.Throws<CsvException>(() => Csv.CreateDataReader(path, readerOptions: invalidSchema));

            // A failed factory must not retain the FileShare.Read handle on Windows
            // or prevent reopening the same file exclusively on any platform.
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(exclusive.CanRead);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FixedWidth_ReaderConstructionFailure_ReleasesFileHandle()
    {
        string path = CreateTempFile(".txt", "ABCD\n");
        try
        {
            // FixedWidthDataReader requires a column definition. The factory opens
            // the file and constructs the streaming reader before checking it.
            Assert.Throws<FixedWidthException>(() => FixedWidth.CreateDataReader(path));

            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(exclusive.CanRead);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Csv_FailedStreamReaderHandoff_PreservesLeaveOpen(bool leaveOpen)
    {
        using var stream = new MemoryStream("Name\nAlice\n"u8.ToArray());
        var invalidSchema = new CsvDataReaderOptions { ColumnNames = [null!] };

        // A streaming reader is already constructed when CsvDataReader rejects ColumnNames.
        Assert.Throws<CsvException>(() => Csv.CreateDataReader(
            stream, readerOptions: invalidSchema, leaveOpen: leaveOpen));

        Assert.Equal(leaveOpen, stream.CanRead);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FixedWidth_FailedStreamReaderHandoff_PreservesLeaveOpen(bool leaveOpen)
    {
        using var stream = new MemoryStream("ABCD\n"u8.ToArray());

        // The streaming reader exists before FixedWidthDataReader requires column definitions.
        Assert.Throws<FixedWidthException>(() => FixedWidth.CreateDataReader(
            stream, leaveOpen: leaveOpen));

        Assert.Equal(leaveOpen, stream.CanRead);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Csv_SuccessfulStreamReaderHandoff_PreservesLeaveOpen(bool leaveOpen)
    {
        using var stream = new MemoryStream("Name\nAlice\n"u8.ToArray());

        using (var reader = Csv.CreateDataReader(stream, leaveOpen: leaveOpen))
        {
            Assert.True(stream.CanRead);
            Assert.True(reader.Read());
            Assert.Equal("Alice", reader.GetString(0));
        }

        Assert.Equal(leaveOpen, stream.CanRead);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FixedWidth_SuccessfulStreamReaderHandoff_PreservesLeaveOpen(bool leaveOpen)
    {
        using var stream = new MemoryStream("ABCD\n"u8.ToArray());
        var schema = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Start = 0, Length = 4, Name = "Code" }]
        };

        using (var reader = FixedWidth.CreateDataReader(stream, readerOptions: schema, leaveOpen: leaveOpen))
        {
            Assert.True(stream.CanRead);
            Assert.True(reader.Read());
            Assert.Equal("ABCD", reader.GetString(0));
        }

        Assert.Equal(leaveOpen, stream.CanRead);
    }

    private static string CreateTempFile(string extension, string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"HeroParser-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, contents);
        return path;
    }
}
