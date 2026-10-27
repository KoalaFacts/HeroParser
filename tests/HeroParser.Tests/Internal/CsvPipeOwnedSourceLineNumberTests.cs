using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait("Category", "Unit")]
public sealed class CsvPipeOwnedSourceLineNumberTests
{
    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 2)]
    public async Task OwnedRows_PreserveSourceLinesThroughQuotedFieldsAndComments(
        bool trackSourceLines, int expectedSecondSourceLine)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("alpha,\"line1\nline2\"\n# comment\nbeta,done\n");
        var options = new CsvReadOptions
        {
            AllowNewlinesInsideQuotes = true,
            CommentCharacter = '#',
            TrackSourceLineNumbers = trackSourceLines
        };
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // Both the contiguous and every split-buffer path must preserve the row's
        // physical source location when converted to the owning async API.
        for (int split = 0; split <= bytes.Length; split++)
        {
            List<(int Row, int SourceLine, string First, string Second)> actual = [];
            await foreach (var row in Csv.ReadFromPipeReaderAsync(
                new CompletedSequenceReader(Split(bytes, split)), options, cancellationToken))
            {
                Assert.Equal(2, row.ColumnCount);
                actual.Add((
                    row.RowNumber,
                    row.SourceLineNumber,
                    row[0].ToUnquotedString(),
                    row[1].ToUnquotedString()));
            }

            Assert.Equal(2, actual.Count);
            Assert.Equal((1, 1, "alpha", "line1\nline2"), actual[0]);
            Assert.Equal((2, expectedSecondSourceLine, "beta", "done"), actual[1]);
        }
    }

    private static ReadOnlySequence<byte> Split(byte[] data, int index)
    {
        var first = new Segment(data.AsMemory(0, index));
        var last = first.Append(data.AsMemory(index));
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

    private sealed class CompletedSequenceReader(ReadOnlySequence<byte> buffer) : PipeReader
    {
        public override void AdvanceTo(SequencePosition consumed) => buffer = buffer.Slice(consumed);
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => AdvanceTo(consumed);
        public override void CancelPendingRead() { }
        public override void Complete(Exception? exception = null) { }
        public override bool TryRead(out ReadResult result)
        {
            result = new ReadResult(buffer, false, true);
            return true;
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ReadResult(buffer, false, true));
    }
}
