using System.Buffers;
using System.Text;
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
