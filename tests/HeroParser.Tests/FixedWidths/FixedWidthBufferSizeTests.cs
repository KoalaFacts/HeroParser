using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Reading.Data;
using HeroParser.FixedWidths.Streaming;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

/// <summary>
/// A non-positive <c>bufferSize</c> selects the minimum buffer, as it already does for the CSV
/// streaming reader. The fixed-width reader used to hand it to <see cref="StreamReader"/>, which
/// threw <see cref="ArgumentOutOfRangeException"/>; the path overload then leaked the open file.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthBufferSizeTests
{
    private const string INPUT = "ONE\nTWO\n";

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(1)]
    public async Task CreateAsyncStreamReader_Stream_AcceptsAnyBufferSize(int bufferSize)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(INPUT), writable: false);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, leaveOpen: true, bufferSize: bufferSize);

        Assert.Equal(["ONE", "TWO"], await ReadAllAsync(reader));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CreateAsyncStreamReader_Path_AcceptsNonPositiveBufferSizeAndReleasesTheFile(int bufferSize)
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, INPUT, TestContext.Current.CancellationToken);

            await using (var reader = FixedWidth.CreateAsyncStreamReader(path, bufferSize: bufferSize))
            {
                Assert.Equal(["ONE", "TWO"], await ReadAllAsync(reader));
            }

            // The reader owned the file stream; nothing may keep it open after disposal.
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(INPUT.Length, exclusive.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateAsyncStreamReader_Path_ClosesTheFileWhenConstructionFails()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, INPUT);

            // The encoding's decoder is requested by the reader constructor, i.e. after the file is opened.
            var error = Assert.Throws<InvalidOperationException>(
                () => FixedWidth.CreateAsyncStreamReader(path, encoding: new DecoderFailingEncoding()));
            Assert.Equal(DecoderFailingEncoding.MESSAGE, error.Message);

            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(INPUT.Length, exclusive.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CreateDataReader_AcceptsNonPositiveBufferSize(int bufferSize)
    {
        var options = new FixedWidthReadOptions { HasHeaderRow = false };
        var readerOptions = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "Value", Start = 0, Length = 3 }]
        };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(INPUT), writable: false);
        using var reader = FixedWidth.CreateDataReader(stream, options, readerOptions, bufferSize: bufferSize);

        var values = new List<string>();
        while (reader.Read())
            values.Add(reader.GetString(0));

        Assert.Equal(["ONE", "TWO"], values);
    }

    private sealed class DecoderFailingEncoding : UTF8Encoding
    {
        public const string MESSAGE = "Decoder creation failed for the test.";

        public override Decoder GetDecoder() => throw new InvalidOperationException(MESSAGE);
    }

    private static async Task<List<string>> ReadAllAsync(FixedWidthAsyncStreamReader reader)
    {
        var records = new List<string>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
            records.Add(reader.Current.RawRecord.ToString());
        return records;
    }
}
