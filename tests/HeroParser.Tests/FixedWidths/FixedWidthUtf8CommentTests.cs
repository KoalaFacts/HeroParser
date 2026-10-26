using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthUtf8CommentTests
{
    [Theory]
    [InlineData('#')]
    [InlineData('é')]
    [InlineData('Ā')]
    [InlineData('漢')]
    public void Utf8ByteSpan_CommentCharacter_MatchesUtf16Text(char marker)
    {
        string input = $"{marker}drop\nKEEP\n";
        var options = new FixedWidthReadOptions
        {
            CommentCharacter = marker,
            TrackSourceLineNumbers = true
        };

        var byteRows = new List<string>();
        var sourceLines = new List<int>();
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(Encoding.UTF8.GetBytes(input), options))
        {
            byteRows.Add(row.ToDecodedString());
            sourceLines.Add(row.SourceLineNumber);
        }

        var textRows = new List<string>();
        foreach (var row in FixedWidth.ReadFromText(input, options))
        {
            textRows.Add(row.RawRecord.ToString());
        }

        Assert.Equal(["KEEP"], byteRows);
        Assert.Equal([2], sourceLines);
        Assert.Equal(textRows, byteRows);
    }

    [Theory]
    [InlineData('é')]
    [InlineData('漢')]
    public async Task PipeReader_UnicodeCommentSplitAcrossSegments_IsSkipped(char marker)
    {
        byte[] markerBytes = Encoding.UTF8.GetBytes(marker.ToString());
        List<ReadOnlyMemory<byte>> chunks = [ReadOnlyMemory<byte>.Empty];
        foreach (byte value in markerBytes)
        {
            byte[] chunk = [value];
            chunks.Add(chunk);
            chunks.Add(ReadOnlyMemory<byte>.Empty);
        }

        chunks.Add("drop\nKEEP\n"u8.ToArray());
        var options = new FixedWidthReadOptions
        {
            CommentCharacter = marker,
            TrackSourceLineNumbers = true
        };

        var rows = new List<string>();
        var sourceLines = new List<int>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(
            new CompletedSequenceReader(FromSegments(chunks.ToArray())),
            options,
            TestContext.Current.CancellationToken))
        {
            rows.Add(row.ToDecodedString());
            sourceLines.Add(row.SourceLineNumber);
        }

        Assert.Equal(["KEEP"], rows);
        Assert.Equal([2], sourceLines);
    }

    [Fact]
    public async Task NonAsciiCommentMarker_DoesNotTreatItsLowByteAsComment()
    {
        const string INPUT = "\0VISIBLE\nĀhidden\nAFTER\n";
        var options = new FixedWidthReadOptions
        {
            CommentCharacter = 'Ā',
            TrackSourceLineNumbers = true
        };
        byte[] utf8 = Encoding.UTF8.GetBytes(INPUT);

        var byteRows = new List<string>();
        var byteLines = new List<int>();
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(utf8, options))
        {
            byteRows.Add(row.ToDecodedString());
            byteLines.Add(row.SourceLineNumber);
        }

        var pipeRows = new List<string>();
        var pipeLines = new List<int>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(
            new CompletedSequenceReader(new ReadOnlySequence<byte>(utf8)),
            options,
            TestContext.Current.CancellationToken))
        {
            pipeRows.Add(row.ToDecodedString());
            pipeLines.Add(row.SourceLineNumber);
        }

        Assert.Equal(["\0VISIBLE", "AFTER"], byteRows);
        Assert.Equal([1, 3], byteLines);
        Assert.Equal(byteRows, pipeRows);
        Assert.Equal(byteLines, pipeLines);
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
