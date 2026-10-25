using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait("Category", "Unit")]
public sealed class CsvPipeEmptySegmentLineNumberTests
{
    [Fact]
    public void CountNewlines_EmptySegmentBetweenCrAndLf_DoesNotCreateAnotherLine()
    {
        ReadOnlySequence<byte> data = FromSegments(
            Encoding.UTF8.GetBytes("first\r"),
            ReadOnlyMemory<byte>.Empty,
            Encoding.UTF8.GetBytes("\nsecond"));

        Assert.False(data.IsSingleSegment);
        Assert.Equal(1, Csv.CountNewlines(data));
    }

    [Fact]
    public async Task BorrowedReader_QuotedCrLfAcrossEmptySegment_PreservesSubsequentSourceLine()
    {
        ReadOnlySequence<byte> data = FromSegments(
            Encoding.UTF8.GetBytes("\"first\r"),
            ReadOnlyMemory<byte>.Empty,
            Encoding.UTF8.GetBytes("\nsecond\",2\nlast,3\n"));

        var options = new CsvReadOptions
        {
            CommentCharacter = '#',
            AllowNewlinesInsideQuotes = true,
            TrackSourceLineNumbers = true
        };

        await using var csv = Csv.CreatePipeSequenceReader(new CompletedSequenceReader(data), options);
        Assert.True(await csv.MoveNextAsync(TestContext.Current.CancellationToken));
        {
            var first = csv.Current;
            Assert.Equal(1, first.RowNumber);
            Assert.Equal(1, first.SourceLineNumber);
            Assert.Equal(2, first.ColumnCount);
            Assert.Equal("first\r\nsecond", first[0].ToString());
            Assert.Equal("2", first[1].ToString());
        }

        Assert.True(await csv.MoveNextAsync(TestContext.Current.CancellationToken));
        {
            var last = csv.Current;
            Assert.Equal(2, last.RowNumber);
            Assert.Equal(3, last.SourceLineNumber);
            Assert.Equal(2, last.ColumnCount);
            Assert.Equal("last", last[0].ToString());
            Assert.Equal("3", last[1].ToString());
        }

        Assert.False(await csv.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    private static ReadOnlySequence<byte> FromSegments(params ReadOnlyMemory<byte>[] chunks)
    {
        var first = new Segment(chunks[0]);
        var last = first;
        for (int index = 1; index < chunks.Length; index++)
        {
            last = last.Append(chunks[index]);
        }

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

    private sealed class CompletedSequenceReader(ReadOnlySequence<byte> data) : PipeReader
    {
        private ReadOnlySequence<byte> remaining = data;

        public override void AdvanceTo(SequencePosition consumed) => remaining = remaining.Slice(consumed);
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => AdvanceTo(consumed);
        public override void CancelPendingRead() { }
        public override void Complete(Exception? exception = null) { }
        public override bool TryRead(out ReadResult result)
        {
            result = new ReadResult(remaining, false, true);
            return true;
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ReadResult(remaining, false, true));
    }
}
