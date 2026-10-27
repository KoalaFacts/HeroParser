using System.Buffers;
using System.IO.Pipelines;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthPipeLeadingEmptySegmentTests
{
    [Fact]
    public void TryGetFirstByte_LeadingEmptySegments_ReturnsFirstDataByte()
    {
        ReadOnlySequence<byte> row = FromSegments(
            ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty,
            "DATA"u8.ToArray());

        Assert.True(FixedWidth.TryGetFirstByte(row, out byte firstByte));
        Assert.Equal((byte)'D', firstByte);
    }

    [Fact]
    public async Task PipeLineReader_LeadingEmptySegments_PreservesDataAndSkipsComment()
    {
        // PipeReader is allowed to return a non-empty sequence whose first segment is empty.
        // The comment probe must find the first actual byte rather than indexing FirstSpan[0].
        ReadOnlySequence<byte> sequence = FromSegments(
            ReadOnlyMemory<byte>.Empty,
            "DATA\r\n"u8.ToArray(),
            ReadOnlyMemory<byte>.Empty,
            "#skip\nNEXT\n"u8.ToArray());

        var options = new FixedWidthReadOptions
        {
            CommentCharacter = '#',
            SkipEmptyLines = false,
            TrackSourceLineNumbers = true
        };

        var rows = new List<string>();
        var sourceLines = new List<int>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(
            new CompletedSequenceReader(sequence),
            options,
            TestContext.Current.CancellationToken))
        {
            rows.Add(row.ToDecodedString());
            sourceLines.Add(row.SourceLineNumber);
        }

        Assert.Equal(["DATA", "NEXT"], rows);
        Assert.Equal([1, 3], sourceLines);
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
