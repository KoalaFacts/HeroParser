using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthCrLfChunkBoundaryTests
{
    [Theory]
    [InlineData("ONE\r\nTWO\r\n", 0, false, "ONE@1|TWO@2")]
    [InlineData("ONE\r\n\r\nTWO\r\n", 0, false, "ONE@1|@2|TWO@3")]
    [InlineData("SKIP\r\nDATA\r\n", 1, false, "DATA@2")]
    [InlineData("SKIP\r\nHEAD\r\nDATA\r\n", 1, true, "DATA@3")]
    [InlineData("#skip\r\nDATA\r\n", 0, false, "DATA@2")]
    [InlineData("ONE\rTWO\r", 0, false, "ONE@1|TWO@2")]
    [InlineData("ONE\r", 0, false, "ONE@1")]
    public async Task MoveNextAsync_CrLfAcrossReads_ParsesPhysicalLinesOnce(
        string input,
        int skipRows,
        bool hasHeaderRow,
        string expectedRows)
    {
        await using var stream = new SingleByteReadStream(input);
        var options = new FixedWidthReadOptions
        {
            SkipRows = skipRows,
            HasHeaderRow = hasHeaderRow,
            SkipEmptyLines = false,
            CommentCharacter = '#',
            TrackSourceLineNumbers = true
        };
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        var actualRows = new List<string>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            var row = reader.Current;
            actualRows.Add(row.RawRecord.ToString() + "@" + row.SourceLineNumber);
        }

        Assert.Equal(expectedRows, string.Join("|", actualRows));
        Assert.True(stream.ReadCalls > 1);
    }

    private sealed class SingleByteReadStream(string input)
        : MemoryStream(Encoding.UTF8.GetBytes(input), writable: false)
    {
        public int ReadCalls { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            return ValueTask.FromResult(base.Read(buffer.Span[..Math.Min(1, buffer.Length)]));
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
