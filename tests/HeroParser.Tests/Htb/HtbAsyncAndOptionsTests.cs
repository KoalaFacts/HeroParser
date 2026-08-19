// The test namespace HeroParser.Tests.Htb shadows the product's HeroParser.Htb gateway —
// members of an enclosing namespace beat using-directive imports — so it is aliased.
using HeroParser.Htbs;
using HeroParser.Htbs.Records;
using HtbApi = HeroParser.Htb;
using HtbReaderBuilder = HeroParser.Htbs.Reading.HtbRecordReaderBuilder<HeroParser.Tests.Htb.HtbAsyncAndOptionsTests.Row>;
using Xunit;

namespace HeroParser.Tests.Htb;

/// <summary>
/// Covers HTB's asynchronous read and write paths and its option surface.
///
/// The synchronous round trip was already tested, but async reading is a separate
/// implementation — every value type has its own ReadXxxAsync, and skipping a row means
/// walking past each column's bytes without decoding them. None of that was exercised,
/// so a truncated read or a mis-sized skip would only have shown up in production.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class HtbAsyncAndOptionsTests : IDisposable
{
    private readonly List<string> tempFiles = [];

    public void Dispose()
    {
        foreach (string path in tempFiles.Where(File.Exists))
        {
            File.Delete(path);
        }
        GC.SuppressFinalize(this);
    }

    private string TempPath(string extension = ".htb")
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        tempFiles.Add(path);
        return path;
    }

    /// <summary>A record touching every HTB data type, including the nullable and array cases.</summary>
    public sealed class Row
    {
        public int Id { get; set; }
        public long Ticks { get; set; }
        public string Name { get; set; } = "";
        public double? Score { get; set; }
        public float Ratio { get; set; }
        public decimal Balance { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid Reference { get; set; }
        public float[]? Embedding { get; set; }
    }

    private static List<Row> Sample(int count = 3) =>
        [.. Enumerable.Range(0, count).Select(i => new Row
        {
            Id = i,
            Ticks = 1_000_000L * i,
            Name = i % 2 == 0 ? $"row-{i}" : $"ünïcödé-{i}",
            Score = i % 3 == 0 ? null : i * 1.5,
            Ratio = i * 0.25f,
            Balance = 100.25m * i,
            IsActive = i % 2 == 0,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
            Reference = new Guid(i, 0, 0, [1, 2, 3, 4, 5, 6, 7, 8]),
            Embedding = i % 4 == 0 ? null : [i * 0.1f, i * -0.2f, i * 0.3f],
        })];

    private static void AssertSame(Row expected, Row actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Ticks, actual.Ticks);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Score, actual.Score);
        Assert.Equal(expected.Ratio, actual.Ratio);
        Assert.Equal(expected.Balance, actual.Balance);
        Assert.Equal(expected.IsActive, actual.IsActive);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.Reference, actual.Reference);
        Assert.Equal(expected.Embedding, actual.Embedding);
    }

    private static async Task<byte[]> WriteAsync(IEnumerable<Row> rows)
    {
        using var stream = new MemoryStream();
        await HtbApi.Write<Row>().ToStreamAsync(stream, rows, leaveOpen: true);
        return stream.ToArray();
    }

    private static async Task<List<Row>> ReadAsync(byte[] bytes, Func<HtbReaderBuilder, HtbReaderBuilder>? configure = null)
    {
        using var stream = new MemoryStream(bytes);
        var builder = HtbApi.Read<Row>();
        if (configure is not null) builder = configure(builder);

        var result = new List<Row>();
        await foreach (var row in builder.FromStreamAsync(stream, leaveOpen: true))
        {
            result.Add(row);
        }
        return result;
    }

    // ---- async round trip ------------------------------------------------------

    [Fact]
    public async Task AsyncRoundTrip_PreservesEveryColumnType()
    {
        var expected = Sample(6);
        var actual = await ReadAsync(await WriteAsync(expected));

        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            AssertSame(expected[i], actual[i]);
        }
    }

    [Fact]
    public async Task AsyncRoundTrip_ThroughFiles()
    {
        string path = TempPath();
        var expected = Sample(4);

        await HtbApi.Write<Row>().ToFileAsync(path, expected);

        var actual = new List<Row>();
        await foreach (var row in HtbApi.Read<Row>().FromFileAsync(path))
        {
            actual.Add(row);
        }

        Assert.Equal(expected.Count, actual.Count);
        AssertSame(expected[0], actual[0]);
    }

    [Fact]
    public async Task AsyncWrite_AcceptsAnAsyncSequence()
    {
        var expected = Sample(3);
        using var stream = new MemoryStream();

        await HtbApi.Write<Row>().ToStreamAsync(stream, AsAsync(expected), leaveOpen: true);

        var actual = await ReadAsync(stream.ToArray());
        Assert.Equal(expected.Count, actual.Count);
        AssertSame(expected[2], actual[2]);

        static async IAsyncEnumerable<Row> AsAsync(IEnumerable<Row> rows)
        {
            foreach (var row in rows)
            {
                await Task.Yield();
                yield return row;
            }
        }
    }

    [Fact]
    public async Task AsyncWrite_ToFileFromAnAsyncSequence()
    {
        string path = TempPath();
        await HtbApi.Write<Row>().ToFileAsync(path, AsAsync(Sample(2)));

        Assert.True(new FileInfo(path).Length > 0, "the file should contain the written rows");

        static async IAsyncEnumerable<Row> AsAsync(IEnumerable<Row> rows)
        {
            foreach (var row in rows)
            {
                await Task.Yield();
                yield return row;
            }
        }
    }

    [Fact]
    public async Task AsyncRoundTrip_EmptySequence_ProducesAReadableFile()
    {
        var actual = await ReadAsync(await WriteAsync([]));
        Assert.Empty(actual);
    }

    // ---- skipping and limits ---------------------------------------------------

    [Fact]
    public async Task SkipRows_WalksPastEveryColumnOfTheSkippedRecords()
    {
        // Skipping cannot decode values, so it has to know each column's byte length —
        // including the variable-length string and float-array columns.
        var expected = Sample(5);
        var actual = await ReadAsync(await WriteAsync(expected), b => b.SkipRows(2));

        Assert.Equal(3, actual.Count);
        AssertSame(expected[2], actual[0]);
        AssertSame(expected[4], actual[2]);
    }

    [Fact]
    public async Task SkipRows_BeyondTheEnd_YieldsNothing()
        => Assert.Empty(await ReadAsync(await WriteAsync(Sample(2)), b => b.SkipRows(10)));

    [Fact]
    public async Task SkipRows_Negative_IsRejected()
    {
        byte[] bytes = await WriteAsync(Sample(1));
        await Assert.ThrowsAsync<HtbException>(() => ReadAsync(bytes, b => b.SkipRows(-1)));
    }

    [Fact]
    public async Task MaxRowCount_BeyondTheFile_ReadsEverything()
        => Assert.Equal(3, (await ReadAsync(await WriteAsync(Sample(3)), b => b.WithMaxRowCount(100))).Count);

    [Fact]
    public async Task MaxRowCount_BelowTheFilesRowCount_IsRejected()
    {
        byte[] bytes = await WriteAsync(Sample(5));
        var ex = await Assert.ThrowsAsync<HtbException>(() => ReadAsync(bytes, b => b.WithMaxRowCount(2)));
        Assert.Equal(HtbErrorCode.LimitExceeded, ex.ErrorCode);
    }

    [Fact]
    public async Task MaxRowCount_NonPositive_IsRejected()
    {
        byte[] bytes = await WriteAsync(Sample(1));
        await Assert.ThrowsAsync<HtbException>(() => ReadAsync(bytes, b => b.WithMaxRowCount(0)));
    }

    [Fact]
    public async Task Write_MaxRowCount_StopsOverlongRuns()
    {
        using var stream = new MemoryStream();
        var ex = await Assert.ThrowsAsync<HtbException>(
            () => HtbApi.Write<Row>().WithMaxRowCount(2).ToStreamAsync(stream, Sample(5), leaveOpen: true));
        Assert.Equal(HtbErrorCode.LimitExceeded, ex.ErrorCode);
    }

    [Fact]
    public async Task Write_MaxOutputSize_StopsOversizedOutput()
    {
        using var stream = new MemoryStream();
        var ex = await Assert.ThrowsAsync<HtbException>(
            () => HtbApi.Write<Row>().WithMaxOutputSize(64).ToStreamAsync(stream, Sample(50), leaveOpen: true));
        Assert.Equal(HtbErrorCode.LimitExceeded, ex.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Write_NonPositiveLimits_AreRejected(int limit)
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<HtbException>(
            () => HtbApi.Write<Row>().WithMaxRowCount(limit).ToStreamAsync(stream, Sample(1), leaveOpen: true));
    }

    [Fact]
    public async Task Write_NonPositiveOutputSize_IsRejected()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<HtbException>(
            () => HtbApi.Write<Row>().WithMaxOutputSize(0).ToStreamAsync(stream, Sample(1), leaveOpen: true));
    }

    // ---- progress --------------------------------------------------------------

    [Fact]
    public async Task Write_ReportsProgress()
    {
        var reports = new List<HtbWriteProgress>();
        using var stream = new MemoryStream();

        await HtbApi.Write<Row>()
            .WithProgress(new SynchronousProgress<HtbWriteProgress>(reports.Add), intervalRows: 1)
            .ToStreamAsync(stream, Sample(4), leaveOpen: true);

        Assert.NotEmpty(reports);
        Assert.Equal(4, reports[^1].RecordsWritten);
        Assert.True(reports[^1].BytesWritten > 0, "the final report should account for the bytes written");
    }

    [Fact]
    public async Task Read_ReportsProgress()
    {
        var reports = new List<HtbProgress>();
        byte[] bytes = await WriteAsync(Sample(4));

        await ReadAsync(bytes, b => b.WithProgress(new SynchronousProgress<HtbProgress>(reports.Add), intervalRows: 1));

        Assert.NotEmpty(reports);
        Assert.Equal(4, reports[^1].RecordsRead);
    }

    [Fact]
    public async Task Read_NonPositiveProgressInterval_IsRejected()
    {
        byte[] bytes = await WriteAsync(Sample(1));
        await Assert.ThrowsAsync<HtbException>(
            () => ReadAsync(bytes, b => b.WithProgress(new SynchronousProgress<HtbProgress>(_ => { }), intervalRows: 0)));
    }

    [Fact]
    public async Task Write_NonPositiveProgressInterval_IsRejected()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<HtbException>(
            () => HtbApi.Write<Row>()
                .WithProgress(new SynchronousProgress<HtbWriteProgress>(_ => { }), intervalRows: 0)
                .ToStreamAsync(stream, Sample(1), leaveOpen: true));
    }

    /// <summary>
    /// Reports on the calling thread. <see cref="Progress{T}"/> posts to the thread pool,
    /// which would let two callbacks append to the same list concurrently.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    // ---- options validation ----------------------------------------------------

    [Fact]
    public void ReadOptions_Defaults()
    {
        var options = HtbReadOptions.Default;
        Assert.Equal(1_000_000, options.MaxRowCount);
        Assert.Equal(0, options.SkipRows);
        Assert.Equal(1000, options.ProgressIntervalRows);
        Assert.Null(options.OnError);
        Assert.Null(options.Progress);
    }

    [Fact]
    public void WriteOptions_Defaults()
    {
        var options = HtbWriteOptions.Default;
        Assert.Null(options.MaxRowCount);
        Assert.Null(options.MaxOutputSize);
        Assert.Equal(1000, options.ProgressIntervalRows);
        Assert.Null(options.OnError);
        Assert.Null(options.Progress);
    }

    // ---- CSV conversion --------------------------------------------------------

    private static HtbSchema TwoColumnSchema() => new(
    [
        new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
        new HtbColumn("Name", HtbDataType.String, isNullable: false),
    ]);

    [Fact]
    public void ConvertFromCsv_AndBackToCsv_RoundTripsThroughFiles()
    {
        string csvIn = TempPath(".csv");
        string htb = TempPath();
        string csvOut = TempPath(".csv");
        File.WriteAllText(csvIn, "Id,Name\n1,Alice\n2,Bob\n");

        HtbApi.ConvertFromCsv(csvIn, htb, TwoColumnSchema());
        HtbApi.ConvertToCsv(htb, csvOut);

        string result = File.ReadAllText(csvOut);
        Assert.Contains("Id,Name", result, StringComparison.Ordinal);
        Assert.Contains("Alice", result, StringComparison.Ordinal);
        Assert.Contains("Bob", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertFromCsvAsync_AndBackToCsvAsync_RoundTripsThroughStreams()
    {
        using var csvIn = new MemoryStream("Id,Name\n7,Carol\n"u8.ToArray());
        using var htb = new MemoryStream();

        await HtbApi.ConvertFromCsvAsync(csvIn, htb, TwoColumnSchema(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(htb.Length > 0, "the conversion should have produced HTB bytes");

        htb.Position = 0;
        using var csvOut = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        await HtbApi.ConvertToCsvAsync(htb, csvOut, cancellationToken: TestContext.Current.CancellationToken);

        string result = csvOut.ToString();
        Assert.Contains("Id,Name", result, StringComparison.Ordinal);
        Assert.Contains("Carol", result, StringComparison.Ordinal);
    }

    // ---- schema ----------------------------------------------------------------

    [Fact]
    public void Schema_RejectsAnEmptyColumnList()
        => Assert.Throws<HtbException>(() => new HtbSchema([]));

    [Fact]
    public void Schema_RejectsABlankColumnName()
        => Assert.Throws<HtbException>(() => new HtbSchema([new HtbColumn(" ", HtbDataType.Int32, false)]));

    [Fact]
    public void Schema_RejectsMoreColumnsThanTheFormatAllows()
    {
        var columns = Enumerable.Range(0, 2049)
            .Select(i => new HtbColumn($"c{i}", HtbDataType.Int32, false))
            .ToList();

        var ex = Assert.Throws<HtbException>(() => new HtbSchema(columns));
        Assert.Contains("2048", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_RejectsANullColumnList()
        => Assert.Throws<ArgumentNullException>(() => new HtbSchema(null!));

    // ---- partial binding -------------------------------------------------------

    /// <summary>Binds only two of the written columns, leaving the rest to be skipped.</summary>
    public sealed class PartialRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    [Fact]
    public async Task AsyncRead_IntoAPartialRecord_SkipsTheUnboundColumns()
    {
        // Skipping cannot decode a value, so it has to advance exactly the right number of
        // bytes per column type — including the length-prefixed string and float array.
        var written = Sample(4);
        byte[] bytes = await WriteAsync(written);

        var read = new List<PartialRow>();
        using var stream = new MemoryStream(bytes);
        await foreach (var row in HtbApi.Read<PartialRow>().FromStreamAsync(stream, leaveOpen: true))
        {
            read.Add(row);
        }

        Assert.Equal(written.Count, read.Count);
        for (int i = 0; i < written.Count; i++)
        {
            Assert.Equal(written[i].Id, read[i].Id);
            Assert.Equal(written[i].Name, read[i].Name);
        }
    }

    [Fact]
    public async Task SyncRead_IntoAPartialRecord_SkipsTheUnboundColumns()
    {
        byte[] bytes = await WriteAsync(Sample(3));

        using var stream = new MemoryStream(bytes);
        var read = HtbApi.Read<PartialRow>().FromStream(stream, leaveOpen: true).ToList();

        Assert.Equal(3, read.Count);
        Assert.Equal("row-0", read[0].Name);
        Assert.Equal(2, read[2].Id);
    }

    [Fact]
    public async Task AsyncRead_PartialRecordWithSkippedRows_StaysAligned()
    {
        // Row skipping and column skipping compose: both have to leave the stream on a
        // record boundary or every later row decodes as garbage.
        var written = Sample(5);
        byte[] bytes = await WriteAsync(written);

        var read = new List<PartialRow>();
        using var stream = new MemoryStream(bytes);
        await foreach (var row in HtbApi.Read<PartialRow>().SkipRows(3).FromStreamAsync(stream, leaveOpen: true))
        {
            read.Add(row);
        }

        Assert.Equal(2, read.Count);
        Assert.Equal(written[3].Name, read[0].Name);
        Assert.Equal(written[4].Id, read[1].Id);
    }

    [Fact]
    public async Task AsyncRead_FromANonSeekableStream_Works()
    {
        // A network or pipe source cannot seek, so skipping has to read and discard.
        byte[] bytes = await WriteAsync(Sample(3));

        var read = new List<PartialRow>();
        using var stream = new ForwardOnlyStream(bytes);
        await foreach (var row in HtbApi.Read<PartialRow>().FromStreamAsync(stream, leaveOpen: true))
        {
            read.Add(row);
        }

        Assert.Equal(3, read.Count);
        Assert.Equal("row-2", read[2].Name);
    }

    /// <summary>A read-only stream that reports itself unseekable, like a socket.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
