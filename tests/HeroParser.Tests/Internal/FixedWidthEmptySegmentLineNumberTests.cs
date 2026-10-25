using System.Buffers;
using System.IO.Pipelines;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthEmptySegmentLineNumberTests
{
    [Fact]
    public void CountNewlines_CrEmptySegmentLf_IsOnePhysicalLine()
    {
        ReadOnlySequence<byte> sequence = FromSegments(
            "A\r"u8.ToArray(),
            ReadOnlyMemory<byte>.Empty,
            "\nXYZ"u8.ToArray());

        Assert.Equal(1, FixedWidthLineScanner.CountNewlines(sequence));
    }

    [Fact]
    public async Task PipeFixedLengthReader_CrEmptySegmentLf_PreservesFollowingSourceLines()
    {
        // The first fixed-width record spans CR, an empty sequence segment, and LF.
        // Its physical CRLF must not advance SourceLineNumber twice.
        ReadOnlySequence<byte> sequence = FromSegments(
            "A\r"u8.ToArray(),
            ReadOnlyMemory<byte>.Empty,
            "\nXYZEND"u8.ToArray());

        var options = new FixedWidthReadOptions
        {
            RecordLength = 3,
            TrackSourceLineNumbers = true
        };

        var records = new List<string>();
        var sourceLines = new List<int>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(
            new CompletedSequenceReader(sequence),
            options,
            TestContext.Current.CancellationToken))
        {
            records.Add(row.ToDecodedString());
            sourceLines.Add(row.SourceLineNumber);
        }

        Assert.Equal(["A\r\n", "XYZ", "END"], records);
        Assert.Equal([1, 2, 2], sourceLines);
    }

    private static ReadOnlySequence<byte> FromSegments(params ReadOnlyMemory<byte>[] chunks)
    {
        var first = new Segment(chunks[0]);
        var last = first;
        for (int i = 1; i < chunks.Length; i++)
        {
            last = last.Append(chunks[i]);
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

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
