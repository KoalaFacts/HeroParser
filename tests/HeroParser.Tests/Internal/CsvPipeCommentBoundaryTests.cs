using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait("Category", "Unit")]
public sealed class CsvPipeCommentBoundaryTests
{
    [Fact]
    public void SegmentedComments_DoNotLetQuotedOrEscapedContentSwallowTheNextRecord()
    {
        string[] comments =
        [
            "# ignored\\\n",
            " \t# unmatched \"\n",
            "\t# escaped \\\" still a comment\n"
        ];

        foreach (string comment in comments)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(comment + "kept,2\n");
            for (int split = 1; split < bytes.Length; split++)
            {
                ReadOnlySequence<byte> buffer = SplitAt(bytes, split);
                Assert.False(buffer.IsSingleSegment);

                Assert.True(Csv.TryReadRow(
                    ref buffer, (byte)'"', (byte)'\\', true, (byte)'#', true, out var skipped));
                Assert.Equal(comment[..^1], Encoding.UTF8.GetString(skipped.ToArray()));

                Assert.True(Csv.TryReadRow(
                    ref buffer, (byte)'"', (byte)'\\', true, (byte)'#', true, out var data));
                Assert.Equal("kept,2", Encoding.UTF8.GetString(data.ToArray()));
                Assert.True(buffer.IsEmpty);
            }
        }
    }

    [Fact]
    public void SegmentedDataLines_StillHonorQuoteAndEscapeRulesWithCommentsEnabled()
    {
        string[] rows =
        [
            "alpha\\\nbeta,4\n",
            "\"alpha\nbeta\",4\n",
            "normal#value,4\n"
        ];

        foreach (string csv in rows)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(csv);
            for (int split = 1; split < bytes.Length; split++)
            {
                ReadOnlySequence<byte> buffer = SplitAt(bytes, split);
                Assert.True(Csv.TryReadRow(
                    ref buffer, (byte)'"', (byte)'\\', true, (byte)'#', true, out var row));
                Assert.Equal(csv[..^1], Encoding.UTF8.GetString(row.ToArray()));
                Assert.True(buffer.IsEmpty);
            }
        }
    }

    [Theory]
    [InlineData(' ', '"', " ignored\"")]
    [InlineData('\t', '"', "\tignored\"")]
    [InlineData('#', ' ', " #ignored")]
    [InlineData('#', '\t', "\t#ignored")]
    public async Task WhitespaceCommentControls_PreserveContiguousReaderResults(
        char commentCharacter, char quote, string comment)
    {
        foreach (string newline in new[] { "\n", "\r\n", "\r" })
        {
            byte[] bytes = Encoding.UTF8.GetBytes(comment + newline + "kept,2" + newline);
            var options = new CsvReadOptions
            {
                CommentCharacter = commentCharacter,
                Quote = quote,
                TrackSourceLineNumbers = true
            };
            var expected = await ReadRows(new ReadOnlySequence<byte>(bytes), options);
            Assert.Equal([("kept", "2", 2, 1, 2)], expected);
            for (int split = 1; split < bytes.Length; split++)
            {
                var actual = await ReadRows(SplitAt(bytes, split), options);
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public async Task EscapedPhysicalLineEndings_KeepSourceLinesInSegmentedReads()
    {
        // A backslash can escape an actual LF or CR inside an unquoted/quoted field.
        // Each such byte is still a physical line ending even though it is not a record boundary.
        bool[] quotedModes = [false, true];
        string[] lineEndings = ["\n", "\r"];
        foreach (bool quoted in quotedModes)
        {
            foreach (string lineEnding in lineEndings)
            {
                string escapedField = $"first\\{lineEnding}second";
                string field = quoted ? $"\"{escapedField}\"" : escapedField;
                byte[] bytes = Encoding.UTF8.GetBytes(field + ",2\nnext,3\n");
                var options = new CsvReadOptions
                {
                    EscapeCharacter = '\\',
                    AllowNewlinesInsideQuotes = quoted,
                    TrackSourceLineNumbers = true
                };

                List<(string First, string Second, int Columns, int Row, int Line)> expected =
                [
                    ($"first{lineEnding}second", "2", 2, 1, 1),
                    ("next", "3", 2, 2, 3)
                ];

                // The contiguous parser already counts source newlines in raw row bytes.
                var contiguous = await ReadRows(new ReadOnlySequence<byte>(bytes), options);
                Assert.Equal(expected, contiguous);
                for (int split = 1; split < bytes.Length; split++)
                {
                    var segmented = await ReadRows(SplitAt(bytes, split), options);
                    Assert.Equal(expected, segmented);
                }
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EscapedCrImmediatelyBeforeTerminatingLf_IsOnePhysicalSourceLine(
        bool enableQuotes, bool enableComments)
    {
        // The escaped CR belongs to the field, and the following LF closes the record.
        // Taken together they are one physical CRLF, not two source lines.
        byte[] bytes = Encoding.UTF8.GetBytes("first,2\\\r\nnext,3\n");
        var options = new CsvReadOptions
        {
            EnableQuotedFields = enableQuotes,
            CommentCharacter = enableComments ? '#' : null,
            EscapeCharacter = '\\',
            TrackSourceLineNumbers = true
        };

        List<(string First, string Second, int Columns, int Row, int Line)> expected =
        [
            ("first", "2\r", 2, 1, 1),
            ("next", "3", 2, 2, 2)
        ];

        // Cover the ordinary contiguous parser and every multi-segment split,
        // especially the split between the escaped CR and its LF terminator.
        Assert.Equal(expected, await ReadRows(new ReadOnlySequence<byte>(bytes), options));
        for (int split = 0; split <= bytes.Length; split++)
        {
            Assert.Equal(expected, await ReadRows(SplitAt(bytes, split), options));
        }

        int afterCr = Array.IndexOf(bytes, (byte)'\r') + 1;
        var first = new Segment(bytes.AsMemory(0, afterCr));
        var last = first.Append(ReadOnlyMemory<byte>.Empty).Append(bytes.AsMemory(afterCr));
        Assert.Equal(expected, await ReadRows(
            new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length), options));
    }

    private static async Task<List<(string First, string Second, int Columns, int Row, int Line)>> ReadRows(
        ReadOnlySequence<byte> buffer, CsvReadOptions options)
    {
        var rows = new List<(string, string, int, int, int)>();
        await using var reader = Csv.CreatePipeSequenceReader(new CompletedSequenceReader(buffer), options);
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            var row = reader.Current;
            rows.Add((row[0].ToString(), row[1].ToString(), row.ColumnCount, row.RowNumber, row.SourceLineNumber));
        }
        return rows;
    }

    private sealed class CompletedSequenceReader(ReadOnlySequence<byte> buffer) : PipeReader
    {
        public override void AdvanceTo(SequencePosition consumed) => buffer = buffer.Slice(consumed);
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => AdvanceTo(consumed);
        public override void CancelPendingRead() { }
        public override void Complete(Exception? exception = null) { }
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => new(new ReadResult(buffer, false, true));
        public override bool TryRead(out ReadResult result)
        {
            result = new ReadResult(buffer, false, true);
            return true;
        }
    }

    private static ReadOnlySequence<byte> SplitAt(byte[] bytes, int split)
    {
        var first = new Segment(bytes.AsMemory(0, split));
        var last = first.Append(bytes.AsMemory(split));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
