using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait("Category", "Unit")]
public sealed class CsvPipeQuotePolicyTests
{
    [Theory]
    [InlineData(true, "a\"b")]
    [InlineData(false, "\"a\"\"b\"")]
    public async Task QuotePolicy_ControlsBothOwnedAndBorrowedColumnDecoding(bool enableQuotes, string expected)
    {
        byte[] data = Encoding.UTF8.GetBytes("\"a\"\"b\",tail\n");
        var options = new CsvReadOptions { EnableQuotedFields = enableQuotes };
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // Exercise every possible two-segment split, not just the contiguous fast path.
        for (int split = 0; split <= data.Length; split++)
        {
            ReadOnlySequence<byte> firstSequence = Split(data, split);
            await using (var borrowed = Csv.CreatePipeSequenceReader(new CompletedSequenceReader(firstSequence), options))
            {
                Assert.True(await borrowed.MoveNextAsync(cancellationToken));
                Assert.Equal(2, borrowed.Current.ColumnCount);
                Assert.Equal(expected, borrowed.Current[0].ToUnquotedString());
                Assert.Equal(expected, borrowed.Current[0].ToString());
                Assert.Equal("tail", borrowed.Current[1].ToUnquotedString());
                Assert.False(await borrowed.MoveNextAsync(cancellationToken));
            }

            var values = new List<string>();
            await foreach (var row in Csv.ReadFromPipeReaderAsync(
                new CompletedSequenceReader(Split(data, split)), options, cancellationToken))
            {
                Assert.Equal(2, row.ColumnCount);
                values.Add(row[0].ToUnquotedString());
                values.Add(row[1].ToString());
            }

            Assert.Equal(2, values.Count);
            Assert.Equal(expected, values[0]);
            Assert.Equal("tail", values[1]);
        }
    }

    [Fact]
    public async Task DisabledQuotes_StillTrimsSurroundingWhitespaceAndUnescapesExplicitEscape()
    {
        byte[] data = Encoding.UTF8.GetBytes("  \"a\"\"b\"  ,  c\\\"d  \n");
        var options = new CsvReadOptions
        {
            EnableQuotedFields = false,
            TrimFields = true,
            EscapeCharacter = '\\'
        };

        await using var reader = Csv.CreatePipeSequenceReader(
            new CompletedSequenceReader(Split(data, data.Length / 2)), options);
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("\"a\"\"b\"", reader.Current[0].ToUnquotedString());
        Assert.Equal("c\"d", reader.Current[1].ToUnquotedString());
        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    private static ReadOnlySequence<byte> Split(byte[] bytes, int index)
    {
        var first = new Segment(bytes.AsMemory(0, index));
        var last = first.Append(bytes.AsMemory(index));
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
        private ReadOnlySequence<byte> remaining = buffer;

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
