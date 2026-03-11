using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Streaming;
using Xunit;

namespace HeroParser.Tests;

public class CoverageGapTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void FileBasedApis_WorkCorrectly()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = "A,B,C\n1,2,3";
            File.WriteAllText(tempFile, content);

            // Test Csv.ReadFromFile
            var reader1 = Csv.ReadFromFile(tempFile, out var bytes1);
            Assert.True(reader1.MoveNext());
            Assert.Equal(3, reader1.Current.ColumnCount);
            Assert.Equal("A", reader1.Current[0].ToString());

            // Test Csv.CreateDataReader(string path)
            using var dr = Csv.CreateDataReader(tempFile);
            Assert.True(dr.Read());
            Assert.Equal("A", dr.GetName(0));
            Assert.Equal("1", dr.GetValue(0));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamReader_CanReadData()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Col1,Col2\nVal1,Val2", TestContext.Current.CancellationToken);
            // Hit: public static CsvAsyncStreamReader CreateAsyncStreamReader(string path, ...)
            await using var reader = Csv.CreateAsyncStreamReader(tempFile);

            // First row (Header)
            Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Col1", reader.Current[0].ToString());

            // Second row (Data)
            Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Val1", reader.Current[0].ToString());
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ReadFromText_StringOverload_Works()
    {
        var csv = "A,B\n1,2";
        // Hits: public static CsvRowReader<char> ReadFromText(string data, CsvReadOptions? options = null)
        var reader = Csv.ReadFromText(csv);
        Assert.True(reader.MoveNext());
        Assert.Equal("A", reader.Current[0].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ReadFromText_WithBytesOutput_Works()
    {
        var csv = "A,B\n1,2";
        // Hits: public static CsvRowReader<byte> ReadFromText(string data, out byte[] textBytes, CsvReadOptions? options = null)
        var reader = Csv.ReadFromText(csv, out var bytes);
        Assert.NotNull(bytes);
        Assert.True(reader.MoveNext());
        Assert.Equal("A", reader.Current[0].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_CharSpan_Works()
    {
        var csv = "A;B;C";
        // Hits: public static char DetectDelimiter(ReadOnlySpan<char> data, int checkLines = 5)
        var delimiter = Csv.DetectDelimiter(csv.AsSpan());
        Assert.Equal(';', delimiter);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_String_Works()
    {
        var csv = "A|B|C";
        // Hits: public static char DetectDelimiter(string data, int sampleRows = 10)
        var delimiter = Csv.DetectDelimiter(csv);
        Assert.Equal('|', delimiter);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ReadFromStream_WorkCorrectly()
    {
        var csv = "A,B\n1,2";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        // Hits: ReadFromStream(Stream, out byte[], options, leaveOpen)
        var reader = Csv.ReadFromStream(ms, out var bytes, options: null, leaveOpen: true);
        Assert.True(reader.MoveNext());
        Assert.Equal("A", reader.Current[0].ToString());
        Assert.True(ms.CanRead); // verify leaveOpen=true
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ReadFromStream_Oversized_Throws()
    {
        var csv = "A,B,C";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        // Hits: ReadFromStream(..., maxBytesToBuffer)
        // Set max buffer to 1 byte, which is less than stream length
        Assert.Throws<CsvException>(() => Csv.ReadFromStream(ms, out _, options: null, leaveOpen: true, maxBytesToBuffer: 1));
    }
}
