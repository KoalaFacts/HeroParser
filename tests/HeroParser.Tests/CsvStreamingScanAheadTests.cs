using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// The streaming readers now scan ahead over their buffered window with the same cursor as the span
/// reader. These tests feed the same bytes through <see cref="Csv.CreateAsyncStreamReader(Stream, CsvReadOptions?, bool, int)"/>
/// and through <see cref="Csv.ReadFromByteSpan"/> and require identical rows, line numbers and errors,
/// with the stream delivered in small, awkward chunks so rows, CRLF pairs and quoted fields straddle
/// buffer refills and batch boundaries.
/// </summary>
[Collection("HardwareCaps")]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class CsvStreamingScanAheadTests
{
    private sealed record RowSnapshot(int LineNumber, int SourceLineNumber, string[] Cells);

    private sealed record ReadOutcome(List<RowSnapshot> Rows, CsvErrorCode? Error, string? Message);

    /// <summary>A stream that returns at most <c>chunk</c> bytes per read, so buffers fill in odd increments.</summary>
    private sealed class TrickleStream(byte[] data, int chunk) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, chunk), data.Length - position);
            Array.Copy(data, position, buffer, offset, n);
            position += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int n = Math.Min(Math.Min(buffer.Length, chunk), data.Length - position);
            data.AsSpan(position, n).CopyTo(buffer.Span);
            position += n;
            return ValueTask.FromResult(n);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static CsvReadOptions Options(bool quotes, bool track, int maxColumns = 16, int? maxFieldSize = null, int? maxRowSize = null) => new()
    {
        EnableQuotedFields = quotes,
        AllowNewlinesInsideQuotes = quotes,
        TrackSourceLineNumbers = track,
        MaxColumnCount = maxColumns,
        MaxRowCount = 1_000_000,
        MaxFieldSize = maxFieldSize,
        MaxRowSize = maxRowSize
    };

    private static RowSnapshot Snapshot(CsvRow<byte> row)
    {
        var cells = new string[row.ColumnCount];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = row[i].ToString();
        return new RowSnapshot(row.LineNumber, row.SourceLineNumber, cells);
    }

    private static ReadOutcome ReadSpan(byte[] utf8, CsvReadOptions options)
    {
        var rows = new List<RowSnapshot>();
        try
        {
            using var reader = Csv.ReadFromByteSpan(utf8, options);
            while (reader.MoveNext())
                rows.Add(Snapshot(reader.Current));
            return new ReadOutcome(rows, null, null);
        }
        catch (CsvException ex)
        {
            return new ReadOutcome(rows, ex.ErrorCode, ex.Message);
        }
    }

    private static async Task<ReadOutcome> ReadStreamAsync(byte[] utf8, CsvReadOptions options, int chunk, int bufferSize)
    {
        var rows = new List<RowSnapshot>();
        try
        {
            using var stream = new TrickleStream(utf8, chunk);
            await using var reader = Csv.CreateAsyncStreamReader(stream, options, leaveOpen: true, bufferSize: bufferSize);
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
                rows.Add(Snapshot(reader.Current));
            return new ReadOutcome(rows, null, null);
        }
        catch (CsvException ex)
        {
            return new ReadOutcome(rows, ex.ErrorCode, ex.Message);
        }
    }

    private static async Task AssertStreamMatchesSpan(string csv, CsvReadOptions options)
    {
        var utf8 = Encoding.UTF8.GetBytes(csv);
        var expected = ReadSpan(utf8, options);

        // Chunk sizes chosen to land refills mid-row, mid-CRLF and mid-quote; 4096 is the reader's minimum buffer.
        foreach (int chunk in new[] { 37, 101, 4096, 65536 })
        {
            var actual = await ReadStreamAsync(utf8, options, chunk, 4096);
            AssertOutcomesEqual(expected, actual, $"chunk={chunk}");
        }
    }

    private static void AssertOutcomesEqual(ReadOutcome expected, ReadOutcome actual, string context)
    {
        Assert.True(expected.Rows.Count == actual.Rows.Count, $"{context}: row count {actual.Rows.Count}, expected {expected.Rows.Count}");
        for (int r = 0; r < expected.Rows.Count; r++)
        {
            var e = expected.Rows[r];
            var a = actual.Rows[r];
            Assert.True(e.LineNumber == a.LineNumber, $"{context}: row {r} LineNumber {a.LineNumber}, expected {e.LineNumber}");
            Assert.True(e.SourceLineNumber == a.SourceLineNumber, $"{context}: row {r} SourceLineNumber {a.SourceLineNumber}, expected {e.SourceLineNumber}");
            Assert.True(e.Cells.Length == a.Cells.Length, $"{context}: row {r} column count {a.Cells.Length}, expected {e.Cells.Length}");
            for (int c = 0; c < e.Cells.Length; c++)
                Assert.True(e.Cells[c] == a.Cells[c], $"{context}: row {r} col {c} '{a.Cells[c]}', expected '{e.Cells[c]}'");
        }

        Assert.True(expected.Error == actual.Error, $"{context}: error {actual.Error}, expected {expected.Error}");
        Assert.True(expected.Message == actual.Message, $"{context}: message '{actual.Message}', expected '{expected.Message}'");
    }

    // ---------------------------------------------------------------------------------------------
    // Inputs (large enough to cross many 4 KB refills and several 4096-int batches)
    // ---------------------------------------------------------------------------------------------

    private static string VaryingRows(string newline, int rows, bool trailingNewline = true)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            int cols = 1 + (r % 7);
            for (int c = 0; c < cols; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append('v').Append(r).Append('_').Append(new string((char)('a' + (c % 26)), (r + c) % 11));
            }
            if (r < rows - 1 || trailingNewline)
                sb.Append(newline);
        }
        return sb.ToString();
    }

    private static string QuotedMix(string newline, int rows)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            sb.Append("id").Append(r).Append(',');
            switch (r % 5)
            {
                case 0: sb.Append("\"plain quoted\""); break;
                case 1: sb.Append("\"has, comma\""); break;
                case 2: sb.Append("\"embedded").Append(newline).Append("newline\""); break;
                case 3: sb.Append("\"doubled \"\"quote\"\" inside\""); break;
                default: sb.Append("unquoted"); break;
            }
            sb.Append(',').Append(new string('z', r % 9)).Append(newline);
        }
        return sb.ToString();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Unquoted_LargeInput_MatchesSpanReader(string newline)
    {
        await AssertStreamMatchesSpan(VaryingRows(newline, 3000), Options(quotes: false, track: false));
        await AssertStreamMatchesSpan(VaryingRows(newline, 3000), Options(quotes: false, track: true));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Quoted_LargeInput_MatchesSpanReader(string newline)
    {
        await AssertStreamMatchesSpan(QuotedMix(newline, 1500), Options(quotes: true, track: false));
        await AssertStreamMatchesSpan(QuotedMix(newline, 1500), Options(quotes: true, track: true));
    }

    [Fact]
    public async Task NoTrailingNewline_BlankLines_MatchSpanReader()
    {
        await AssertStreamMatchesSpan(VaryingRows("\n", 500, trailingNewline: false), Options(quotes: true, track: true));
        await AssertStreamMatchesSpan("\n\n" + VaryingRows("\r\n", 200) + "\r\n\r\n" + VaryingRows("\n", 200) + "\n\n\n", Options(quotes: true, track: true));
        await AssertStreamMatchesSpan("single,row,no,newline", Options(quotes: true, track: true));
        await AssertStreamMatchesSpan("", Options(quotes: true, track: true));
    }

    [Fact]
    public async Task Utf8Bom_IsStrippedOnTheBatchPath()
    {
        var body = VaryingRows("\n", 300);
        var withBom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(body)).ToArray();
        var expected = ReadSpan(Encoding.UTF8.GetBytes(body), Options(quotes: true, track: true));
        var actual = await ReadStreamAsync(withBom, Options(quotes: true, track: true), chunk: 37, bufferSize: 4096);
        AssertOutcomesEqual(expected, actual, "bom");
    }

    [Fact]
    public async Task Errors_MatchSpanReader_AfterSameRows()
    {
        string unterminated = VaryingRows("\n", 400) + "bad,\"never closed,x\n" + "more,rows\n";
        await AssertStreamMatchesSpan(unterminated, Options(quotes: true, track: true));

        string tooMany = VaryingRows("\n", 400) + "1,2,3,4,5,6,7,8,9\n" + "after,error\n";
        await AssertStreamMatchesSpan(tooMany, Options(quotes: false, track: true, maxColumns: 8));

        string longField = VaryingRows("\n", 400) + "short," + new string('L', 40) + ",short\n";
        await AssertStreamMatchesSpan(longField, Options(quotes: true, track: true, maxFieldSize: 20));
    }

    [Fact]
    public async Task MaxRowSize_StillEnforced_OnBatchedRows()
    {
        string csv = VaryingRows("\n", 100) + "x," + new string('y', 5000) + "\n" + "a,b\n";
        var utf8 = Encoding.UTF8.GetBytes(csv);
        var outcome = await ReadStreamAsync(utf8, Options(quotes: false, track: false, maxRowSize: 1000), chunk: 4096, bufferSize: 4096);
        Assert.Equal(CsvErrorCode.ParseError, outcome.Error);
        Assert.Equal(100, outcome.Rows.Count);
    }

    [Fact]
    public async Task SkipRows_AppliesToBatchedRows()
    {
        string csv = VaryingRows("\n", 50);
        var utf8 = Encoding.UTF8.GetBytes(csv);
        using var stream = new MemoryStream(utf8);
        await using var reader = Csv.Read().SkipRows(10).FromStreamAsync(stream);
        int n = 0;
        int firstLineNumber = -1;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            if (n == 0) firstLineNumber = reader.Current.LineNumber;
            n++;
        }
        Assert.Equal(40, n);
        Assert.Equal(11, firstLineNumber);
    }

    [Fact]
    public async Task CommentCharacter_KeepsPerRowPath_OnStream()
    {
        var options = new CsvReadOptions { CommentCharacter = '#', TrackSourceLineNumbers = true, MaxColumnCount = 16 };
        Assert.False(CsvRowBatchScanner.IsSupported(options));
        var utf8 = Encoding.UTF8.GetBytes("# c\na,b\n  # c2\nc,d\n");
        var outcome = await ReadStreamAsync(utf8, options, chunk: 5, bufferSize: 4096);
        Assert.Null(outcome.Error);
        Assert.Equal(2, outcome.Rows.Count);
        Assert.Equal(["a", "b"], outcome.Rows[0].Cells);
        Assert.Equal(4, outcome.Rows[1].SourceLineNumber);
    }

    /// <summary>
    /// Without AVX2/AVX-512 (Apple Silicon, or SIMD disabled) both readers take the per-row parser; the
    /// streaming reader must still agree with the span reader across refills, including CRLF pairs split
    /// by a read boundary.
    /// </summary>
    [Fact]
    public async Task PerRowFallback_NoSimd_MatchesSpanReader()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        Assert.False(CsvRowBatchScanner.IsSupported(Options(quotes: true, track: true)));
        await AssertStreamMatchesSpan(VaryingRows("\r\n", 600), Options(quotes: false, track: true));
        await AssertStreamMatchesSpan(QuotedMix("\r\n", 300), Options(quotes: true, track: true));
        await AssertStreamMatchesSpan("\n\n" + VaryingRows("\r\n", 200) + "\r\n\r\n" + VaryingRows("\n", 100) + "\n\n\n", Options(quotes: true, track: true));

        // Limits must be judged on the complete row, not on the part of it a refill happened to cut.
        await AssertStreamMatchesSpan(VaryingRows("\n", 400) + "short," + new string('L', 40) + ",short\n", Options(quotes: true, track: true, maxFieldSize: 20));
        await AssertStreamMatchesSpan(VaryingRows("\n", 400) + "1,2,3,4,5,6,7,8,9\n" + "after,error\n", Options(quotes: false, track: true, maxColumns: 8));
        await AssertStreamMatchesSpan(VaryingRows("\n", 400) + "bad,\"never closed,x\n" + "more,rows\n", Options(quotes: true, track: true));
    }

    // ---------------------------------------------------------------------------------------------
    // Multi-schema streaming reader (UTF-16 buffer): records from the stream match records from text.
    // ---------------------------------------------------------------------------------------------

    [GenerateBinder]
    public sealed class StreamFoo
    {
        public string? Type { get; set; }
        public int A { get; set; }
        public string? B { get; set; }
    }

    [GenerateBinder]
    public sealed class StreamBar
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public async Task MultiSchemaStreaming_MatchesText_AcrossRefills()
    {
        var sb = new StringBuilder("Type,A,B\n");
        for (int i = 0; i < 2000; i++)
        {
            if (i % 3 == 0) sb.Append("Bar,").Append(i).Append(",\"name, ").Append(i).Append("\"\n");
            else sb.Append("Foo,").Append(i).Append(',').Append(new string('b', i % 13)).Append(i % 2 == 0 ? "\r\n" : "\n");
        }
        string csv = sb.ToString();

        var fromText = new List<string>();
        foreach (var rec in Csv.Read().WithMultiSchema().WithDiscriminator("Type").MapRecord<StreamFoo>("Foo").MapRecord<StreamBar>("Bar").FromText(csv))
            fromText.Add(Describe(rec));

        var fromStream = new List<string>();
        using var stream = new TrickleStream(Encoding.UTF8.GetBytes(csv), 53);
        await using var reader = Csv.Read().WithMultiSchema().WithDiscriminator("Type").MapRecord<StreamFoo>("Foo").MapRecord<StreamBar>("Bar").FromStream(stream, leaveOpen: true);
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
            fromStream.Add(Describe(reader.Current!));

        Assert.Equal(2000, fromText.Count);
        Assert.Equal(fromText, fromStream);

        static string Describe(object rec) => rec switch
        {
            StreamFoo f => $"Foo:{f.A}:{f.B}",
            StreamBar b => $"Bar:{b.Name}",
            _ => rec.GetType().Name
        };
    }
}
