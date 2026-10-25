using HeroParser.FixedWidths;
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

    private static string CreateTempFile(string extension, string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"HeroParser-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, contents);
        return path;
    }
}
