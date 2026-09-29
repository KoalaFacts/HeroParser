using System.Buffers;
using System.Text;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait("Category", "Unit")]
public class CsvPipeColumnDecodingTests
{
    [Theory]
    [InlineData("", "", '"', null)]
    [InlineData("\"\"", "", '"', null)]
    [InlineData("\"plain\"", "plain", '"', null)]
    [InlineData("a\"b", "a\"b", '"', null)]
    [InlineData("\"a\"\"b\"", "a\"b", '"', null)]
    [InlineData("\"a\\\"b\"", "a\"b", '"', '\\')]
    [InlineData("a\\\\b", "a\\b", '"', '\\')]
    [InlineData("a\\", "a\\", '"', '\\')]
    [InlineData("a\\\"\"b", "a\"\"b", '"', '\\')]
    [InlineData("a\"b", "ab", '"', '"')]
    [InlineData("'a''b'", "a'b", '\'', null)]
    [InlineData("\"\u4F60\u597D \"\"\u4E16\u754C\"\" \uD83D\uDE00\"", "\u4F60\u597D \"\u4E16\u754C\" \uD83D\uDE00", '"', null)]
    [InlineData("\\\uD83D\uDE00", "\uD83D\uDE00", '"', '\\')]
    public void Decoding_PreservesTextAtEverySegmentBoundary(string text, string expected, char quote, char? escape)
    {
        AssertColumns(Encoding.UTF8.GetBytes(text), expected, (byte)quote, escape.HasValue ? (byte)escape.Value : null);
    }

    [Fact]
    public void InvalidUtf8_IsDecodedBeforeEscapeRemoval()
    {
        AssertColumns([0xC2, (byte)'\\', 0x80], "\uFFFD\uFFFD", (byte)'"', (byte)'\\');
        AssertColumns([0xF0, 0x9F, 0x98], "\uFFFD", (byte)'"', null);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(509)]
    [InlineData(510)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(4096)]
    public void LargeEscapedColumns_PreserveContent(int length)
    {
        string prefix = new('a', length - 3);
        AssertColumns(Encoding.UTF8.GetBytes(prefix + "\"\"z"), prefix + "\"z", (byte)'"', null, everySplit: false);
    }

    [Fact]
    public void UnicodeAcrossManySegments_IncludingEmptySegments_PreservesText()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\"\u4F60\u597D \"\"\u4E16\u754C\"\" \uD83D\uDE00\"");
        var first = new Segment(ReadOnlyMemory<byte>.Empty);
        var last = first;
        for (int i = 0; i < bytes.Length; i++)
        {
            last = last.Append(bytes.AsMemory(i, 1)).Append(ReadOnlyMemory<byte>.Empty);
        }

        var sequence = new ReadOnlySequence<byte>(first, 0, last, 0);
        Assert.Equal("\u4F60\u597D \"\u4E16\u754C\" \uD83D\uDE00", new CsvPipeSequenceColumn(sequence, (byte)'"', null).ToUnquotedString());
    }

    [Fact]
    public void DecodedStrings_RemainOwnedAfterPoolBuffersAreReused()
    {
        string prefix = new('a', 4096);
        byte[] bytes = Encoding.UTF8.GetBytes(prefix + "\"\"z");
        var first = new Segment(bytes.AsMemory(0, bytes.Length / 2));
        var last = first.Append(bytes.AsMemory(bytes.Length / 2));
        var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        string result = new CsvPipeSequenceColumn(sequence, (byte)'"', null).ToUnquotedString();
        bytes.AsSpan().Fill((byte)'x');
        for (int i = 0; i < 16; i++)
        {
            _ = new CsvPipeSequenceColumn(sequence, (byte)'x', null).ToUnquotedString();
        }

        Assert.Equal(prefix + "\"z", result);
    }

    private static void AssertColumns(byte[] bytes, string expected, byte quote, byte? escape, bool everySplit = true)
    {
        var owned = new CsvPipeColumn(bytes, 0, bytes.Length, quote, escape);
        Assert.Equal(expected, owned.ToUnquotedString());
        Assert.Equal(expected, owned.ToString());
        byte[] padded = [0, 0, .. bytes, 0];
        Assert.Equal(expected, new CsvPipeColumn(padded, 2, bytes.Length, quote, escape).ToUnquotedString());
        Assert.Equal(expected, new CsvPipeSequenceColumn(new ReadOnlySequence<byte>(bytes), quote, escape).ToUnquotedString());

        int step = everySplit ? 1 : Math.Max(1, bytes.Length / 8);
        for (int split = 0; split <= bytes.Length; split += step)
        {
            var first = new Segment(bytes.AsMemory(0, split));
            var last = first.Append(bytes.AsMemory(split));
            var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
            var borrowed = new CsvPipeSequenceColumn(sequence, quote, escape);
            Assert.Equal(expected, borrowed.ToUnquotedString());
            Assert.Equal(expected, borrowed.ToString());
        }
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}
