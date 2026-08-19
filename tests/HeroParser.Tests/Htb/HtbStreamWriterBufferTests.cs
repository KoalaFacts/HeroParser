// The test namespace HeroParser.Tests.Htb shadows the product's HeroParser.Htb gateway,
// so the gateway is aliased. See HtbAsyncAndOptionsTests for the same note.
using HeroParser.Htbs;
using HeroParser.Htbs.Records;
using HtbApi = HeroParser.Htb;
using Xunit;

namespace HeroParser.Tests.Htb;

/// <summary>
/// Covers the HTB writer's paths for values larger than its 16 KB write buffer.
///
/// Strings, column names and float arrays are all length-prefixed and copied into that
/// buffer; when one does not fit, the writer flushes and streams the value through a
/// pooled array instead. Every existing test writes small values, so those branches — the
/// ones that decide where a large value's bytes actually go — had never run, and getting
/// one wrong corrupts the file rather than failing loudly.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class HtbStreamWriterBufferTests
{
    /// <summary>Matches the writer's internal 16 KB buffer.</summary>
    private const int BUFFER_SIZE = 16 * 1024;

    public sealed class BigRow
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public float[]? Values { get; set; }
    }

    private static async Task<List<BigRow>> RoundTripAsync(IEnumerable<BigRow> records)
    {
        using var stream = new MemoryStream();
        await HtbApi.Write<BigRow>().ToStreamAsync(stream, records, leaveOpen: true);

        stream.Position = 0;
        var read = new List<BigRow>();
        await foreach (var row in HtbApi.Read<BigRow>().FromStreamAsync(stream, leaveOpen: true))
        {
            read.Add(row);
        }
        return read;
    }

    [Fact]
    public async Task StringLongerThanTheBuffer_SurvivesTheRoundTrip()
    {
        string text = new('x', BUFFER_SIZE * 3);
        var read = await RoundTripAsync([new BigRow { Id = 1, Text = text }]);

        var row = Assert.Single(read);
        Assert.Equal(text.Length, row.Text.Length);
        Assert.Equal(text, row.Text);
    }

    [Fact]
    public async Task StringExactlyTheBufferLength_SurvivesTheRoundTrip()
    {
        // The boundary decides between the copy path and the streamed one.
        string text = new('y', BUFFER_SIZE);
        var read = await RoundTripAsync([new BigRow { Id = 2, Text = text }]);

        Assert.Equal(text, Assert.Single(read).Text);
    }

    [Fact]
    public async Task MultiByteCharacters_AreSizedByTheirEncodedLength()
    {
        // A string's byte length is not its character count, so the buffer decision has to
        // be made on the UTF-8 size or the value is truncated.
        string text = string.Concat(Enumerable.Repeat("日本語", BUFFER_SIZE / 2));
        var read = await RoundTripAsync([new BigRow { Id = 3, Text = text }]);

        Assert.Equal(text, Assert.Single(read).Text);
    }

    [Fact]
    public async Task FloatArrayLargerThanTheBuffer_SurvivesTheRoundTrip()
    {
        float[] values = [.. Enumerable.Range(0, BUFFER_SIZE).Select(i => i * 0.5f)];
        var read = await RoundTripAsync([new BigRow { Id = 4, Values = values }]);

        var row = Assert.Single(read);
        Assert.NotNull(row.Values);
        Assert.Equal(values.Length, row.Values.Length);
        Assert.Equal(values, row.Values);
    }

    [Fact]
    public async Task EmptyFloatArray_IsDistinctFromNull()
    {
        var read = await RoundTripAsync([
            new BigRow { Id = 5, Values = [] },
            new BigRow { Id = 6, Values = null },
        ]);

        Assert.Equal(2, read.Count);
        Assert.NotNull(read[0].Values);
        Assert.Empty(read[0].Values!);
        Assert.Null(read[1].Values);
    }

    [Fact]
    public async Task ManyRowsSpanningManyFlushes_AllSurvive()
    {
        // Each row is a few hundred bytes, so the buffer fills and drains repeatedly.
        var records = Enumerable.Range(0, 500)
            .Select(i => new BigRow { Id = i, Text = new string('z', 300), Values = [i] })
            .ToList();

        var read = await RoundTripAsync(records);

        Assert.Equal(records.Count, read.Count);
        Assert.Equal(499, read[^1].Id);
        Assert.Equal(300, read[^1].Text.Length);
    }

    // ---- header ----------------------------------------------------------------

    private static HtbSchema SchemaWith(string columnName) => new(
    [
        new HtbColumn(columnName, HtbDataType.String, isNullable: false),
    ]);

    [Fact]
    public void ColumnNameLongerThanTheBuffer_IsWrittenWhole()
    {
        // Column names go through the same length-prefixed copy as values do.
        string name = new('n', BUFFER_SIZE * 2);

        using var stream = new MemoryStream();
        using (var writer = new HeroParser.Htbs.Writing.HtbStreamWriter(stream, leaveOpen: true))
        {
            writer.WriteHeader(SchemaWith(name));
            writer.Flush();
        }

        // Magic (4) + column count (4) + type/nullable/length prefix + the name itself.
        Assert.True(stream.Length > name.Length, "the header should contain the whole column name");
    }

    [Fact]
    public void WritingTheHeaderTwice_IsRejected()
    {
        using var stream = new MemoryStream();
        using var writer = new HeroParser.Htbs.Writing.HtbStreamWriter(stream, leaveOpen: true);
        writer.WriteHeader(SchemaWith("Name"));

        var ex = Assert.Throws<HtbException>(() => writer.WriteHeader(SchemaWith("Name")));
        Assert.Equal(HtbErrorCode.SerializationError, ex.ErrorCode);
    }

    [Fact]
    public void NullSchema_IsRejected()
    {
        using var stream = new MemoryStream();
        using var writer = new HeroParser.Htbs.Writing.HtbStreamWriter(stream, leaveOpen: true);

        Assert.Throws<ArgumentNullException>(() => writer.WriteHeader(null!));
    }

    [Fact]
    public void NullStream_IsRejected()
        => Assert.Throws<ArgumentNullException>(() => new HeroParser.Htbs.Writing.HtbStreamWriter(null!));

    [Fact]
    public void BytesWritten_TracksBufferedAndFlushedOutput()
    {
        using var stream = new MemoryStream();
        using var writer = new HeroParser.Htbs.Writing.HtbStreamWriter(stream, leaveOpen: true);

        Assert.Equal(0, writer.BytesWritten);
        writer.WriteHeader(SchemaWith("Name"));

        // Counted before the flush, so buffered bytes are included.
        Assert.True(writer.BytesWritten > 0, "buffered bytes should be counted");
        writer.Flush();
        Assert.Equal(stream.Length, writer.BytesWritten);
    }

    [Fact]
    public void DisposedWriter_RejectsFurtherHeaderWrites()
    {
        using var stream = new MemoryStream();
        var writer = new HeroParser.Htbs.Writing.HtbStreamWriter(stream, leaveOpen: true);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.WriteHeader(SchemaWith("Name")));
    }
}
