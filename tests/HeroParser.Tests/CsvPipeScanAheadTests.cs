using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Differential tests for the PipeReader readers' scan-ahead: whatever mix of batched single-segment
/// windows and per-row multi-segment fallbacks a pipe produces, the rows, row numbers, source line
/// numbers and errors must match the scalar per-row span reader. Small pipe segments and a trickling
/// stream force rows to straddle segment and read boundaries.
/// </summary>
[Collection("HardwareCaps")]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class CsvPipeScanAheadTests
{
    private sealed record RowSnapshot(int RowNumber, int SourceLineNumber, string[] Cells);

    private sealed record ReadOutcome(List<RowSnapshot> Rows, CsvErrorCode? Error);

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

    private static CsvReadOptions Options(
        bool quotes = true,
        bool track = true,
        int maxColumns = 16,
        int? maxFieldSize = null,
        int maxRows = 1_000_000,
        int? maxRowSize = null,
        bool trim = false,
        bool useSimd = true) => new()
        {
            EnableQuotedFields = quotes,
            AllowNewlinesInsideQuotes = quotes,
            TrackSourceLineNumbers = track,
            MaxColumnCount = maxColumns,
            MaxRowCount = maxRows,
            MaxFieldSize = maxFieldSize,
            MaxRowSize = maxRowSize,
            TrimFields = trim,
            UseSimdIfAvailable = useSimd
        };

    private static CsvReadOptions WithoutSimd(CsvReadOptions o) => new()
    {
        EnableQuotedFields = o.EnableQuotedFields,
        AllowNewlinesInsideQuotes = o.AllowNewlinesInsideQuotes,
        TrackSourceLineNumbers = o.TrackSourceLineNumbers,
        MaxColumnCount = o.MaxColumnCount,
        MaxRowCount = o.MaxRowCount,
        MaxFieldSize = o.MaxFieldSize,
        MaxRowSize = o.MaxRowSize,
        TrimFields = o.TrimFields,
        UseSimdIfAvailable = false
    };

    private static ReadOutcome ReadSpanOracle(byte[] utf8, CsvReadOptions options)
    {
        var rows = new List<RowSnapshot>();
        try
        {
            using var reader = Csv.ReadFromByteSpan(utf8, WithoutSimd(options));
            while (reader.MoveNext())
            {
                var row = reader.Current;
                var cells = new string[row.ColumnCount];
                for (int i = 0; i < cells.Length; i++)
                    cells[i] = row[i].ToString();
                rows.Add(new RowSnapshot(row.LineNumber, row.SourceLineNumber, cells));
            }
            return new ReadOutcome(rows, null);
        }
        catch (CsvException ex)
        {
            return new ReadOutcome(rows, ex.ErrorCode);
        }
    }

    private static PipeReader CreatePipe(byte[] utf8, int chunk, int segment) =>
        PipeReader.Create(new TrickleStream(utf8, chunk), new StreamPipeReaderOptions(bufferSize: segment, minimumReadSize: Math.Min(segment, 16), leaveOpen: false));

    private static async Task<ReadOutcome> ReadSequenceAsync(byte[] utf8, CsvReadOptions options, int chunk, int segment)
    {
        var rows = new List<RowSnapshot>();
        try
        {
            await using var reader = Csv.CreatePipeSequenceReader(CreatePipe(utf8, chunk, segment), options);
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
            {
                var row = reader.Current;
                var cells = new string[row.ColumnCount];
                for (int i = 0; i < cells.Length; i++)
                    cells[i] = Encoding.UTF8.GetString(row[i].ToArray()); // raw cell, quotes included, like the oracle
                rows.Add(new RowSnapshot(row.RowNumber, row.SourceLineNumber, cells));
            }
            return new ReadOutcome(rows, null);
        }
        catch (CsvException ex)
        {
            return new ReadOutcome(rows, ex.ErrorCode);
        }
    }

    private static async Task<ReadOutcome> ReadOwnedAsync(byte[] utf8, CsvReadOptions options, int chunk, int segment)
    {
        var rows = new List<RowSnapshot>();
        try
        {
            await foreach (var row in Csv.ReadFromPipeReaderAsync(CreatePipe(utf8, chunk, segment), options, TestContext.Current.CancellationToken))
            {
                var cells = new string[row.ColumnCount];
                for (int i = 0; i < cells.Length; i++)
                    cells[i] = Encoding.UTF8.GetString(row[i].Span); // raw cell, quotes included, like the oracle
                rows.Add(new RowSnapshot(row.RowNumber, row.RowNumber, cells));
            }
            return new ReadOutcome(rows, null);
        }
        catch (CsvException ex)
        {
            return new ReadOutcome(rows, ex.ErrorCode);
        }
    }

    private static void AssertSameRows(ReadOutcome expected, ReadOutcome actual, string label, bool compareSourceLines = true)
    {
        Assert.True(expected.Error == actual.Error, $"{label}: error {actual.Error} != oracle {expected.Error}");
        Assert.True(expected.Rows.Count == actual.Rows.Count, $"{label}: {actual.Rows.Count} rows != oracle {expected.Rows.Count}");
        for (int i = 0; i < expected.Rows.Count; i++)
        {
            var e = expected.Rows[i];
            var a = actual.Rows[i];
            Assert.True(e.RowNumber == a.RowNumber, $"{label}: row {i} number {a.RowNumber} != {e.RowNumber}");
            if (compareSourceLines)
                Assert.True(e.SourceLineNumber == a.SourceLineNumber, $"{label}: row {i} source line {a.SourceLineNumber} != {e.SourceLineNumber}");
            Assert.True(e.Cells.AsSpan().SequenceEqual(a.Cells), $"{label}: row {i} cells differ: [{string.Join("|", a.Cells)}] != [{string.Join("|", e.Cells)}]");
        }
    }

    private static readonly (int Chunk, int Segment)[] Shapes =
    [
        (37, 64),       // tiny reads, tiny segments: rows straddle constantly
        (101, 256),
        (4096, 4096),   // default-ish
        (65536, 65536), // one segment holds everything
    ];

    private static async Task AssertPipeMatchesOracle(string csv, CsvReadOptions options)
    {
        var utf8 = Encoding.UTF8.GetBytes(csv);
        var expected = ReadSpanOracle(utf8, options);
        foreach (var (chunk, segment) in Shapes)
        {
            string label = $"chunk={chunk} segment={segment} track={options.TrackSourceLineNumbers} quotes={options.EnableQuotedFields}";
            AssertSameRows(expected, await ReadSequenceAsync(utf8, options, chunk, segment), label + " sequence");
            AssertSameRows(expected, await ReadOwnedAsync(utf8, options, chunk, segment), label + " owned", compareSourceLines: false);
        }
    }

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
    [InlineData("\r")]
    public async Task Unquoted_AllNewlines_MatchOracle(string newline)
    {
        await AssertPipeMatchesOracle(VaryingRows(newline, 400), Options(quotes: false, track: true));
        await AssertPipeMatchesOracle(VaryingRows(newline, 400), Options(quotes: true, track: false));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task QuotedMix_MatchOracle(string newline)
    {
        await AssertPipeMatchesOracle(QuotedMix(newline, 300), Options(quotes: true, track: true));
        await AssertPipeMatchesOracle(QuotedMix(newline, 300), Options(quotes: true, track: false));
    }

    [Fact]
    public async Task FinalRowWithoutNewline_And_BlankLines_MatchOracle()
    {
        await AssertPipeMatchesOracle(VaryingRows("\n", 50, trailingNewline: false), Options(track: true));
        await AssertPipeMatchesOracle("a,b\n\n\nc,d\r\n\r\ne,f", Options(track: true));
        await AssertPipeMatchesOracle("\n\n\n", Options(track: true));
        await AssertPipeMatchesOracle("single,row,no,newline", Options(track: true));
    }

    [Fact]
    public async Task CrlfOnSegmentEdges_MatchOracle()
    {
        // CR as the last byte of a 64-byte segment with the LF in the next one, repeatedly.
        var sb = new StringBuilder();
        for (int r = 0; r < 60; r++)
            sb.Append(new string('x', 62 - (r % 3))).Append("\r\n");
        await AssertPipeMatchesOracle(sb.ToString(), Options(quotes: true, track: true, maxColumns: 4));
        await AssertPipeMatchesOracle(sb.ToString(), Options(quotes: false, track: true, maxColumns: 4));
    }

    [Fact]
    public async Task TrimFields_MatchOracle()
    {
        await AssertPipeMatchesOracle("  a , b  ,\" q \"\n c ,d, e \n", Options(trim: true));
    }

    [Fact]
    public async Task LimitViolations_MatchOracle()
    {
        await AssertPipeMatchesOracle(VaryingRows("\n", 40), Options(maxColumns: 4));
        await AssertPipeMatchesOracle(VaryingRows("\n", 40), Options(maxFieldSize: 6));
        await AssertPipeMatchesOracle(VaryingRows("\n", 40), Options(maxRows: 10));
        await AssertPipeMatchesOracle("a,b\n\"unterminated,c\n", Options());
        await AssertPipeMatchesOracle(QuotedMix("\n", 20), new CsvReadOptions { EnableQuotedFields = true, AllowNewlinesInsideQuotes = false, MaxColumnCount = 16 });
    }

    [Fact]
    public async Task MaxRowSize_OnStraddlingRow_ThrowsLikeBefore()
    {
        string csv = "short,row\n" + new string('y', 500) + ",tail\nlast,row\n";
        var options = Options(maxRowSize: 200);
        var utf8 = Encoding.UTF8.GetBytes(csv);
        foreach (var (chunk, segment) in Shapes)
        {
            var outcome = await ReadSequenceAsync(utf8, options, chunk, segment);
            Assert.True(outcome.Error is not null, $"chunk={chunk} segment={segment}: expected a row-size error");
            Assert.Single(outcome.Rows);
        }
    }

    [Fact]
    public async Task SkipRows_ThroughTypedReader_MatchOracleCount()
    {
        // The typed pipe reader passes skipRows to the sequence reader; batched rows must honour it too.
        var utf8 = Encoding.UTF8.GetBytes(VaryingRows("\n", 30));
        await using var reader = new CsvPipeSequenceReader(CreatePipe(utf8, 4096, 4096), Options(track: true), skipRows: 3);
        int count = 0;
        int firstRowNumber = -1;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            if (firstRowNumber < 0) firstRowNumber = reader.Current.RowNumber;
            count++;
        }
        Assert.Equal(27, count);
        Assert.Equal(4, firstRowNumber);
    }

    [Fact]
    public async Task OwnedRows_SurviveAdvancing()
    {
        // ToOwnedRow copies batch rows out of the shared window; the copies must stay correct after MoveNext.
        var utf8 = Encoding.UTF8.GetBytes(VaryingRows("\n", 200));
        var owned = new List<CsvPipeRow>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(CreatePipe(utf8, 101, 256), Options(), TestContext.Current.CancellationToken))
            owned.Add(row);

        var expected = ReadSpanOracle(utf8, Options());
        Assert.Equal(expected.Rows.Count, owned.Count);
        for (int i = 0; i < owned.Count; i++)
        {
            var cells = new string[owned[i].ColumnCount];
            for (int c = 0; c < cells.Length; c++)
                cells[c] = Encoding.UTF8.GetString(owned[i][c].Span);
            Assert.Equal(expected.Rows[i].Cells, cells);
        }
    }

    [Fact]
    public async Task NoSimd_StillMatchesOracle()
    {
        await AssertPipeMatchesOracle(QuotedMix("\r\n", 100), Options(quotes: true, track: true, useSimd: false));
    }
}
