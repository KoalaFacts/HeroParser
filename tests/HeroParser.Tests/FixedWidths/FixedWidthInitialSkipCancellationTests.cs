using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthInitialSkipCancellationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MoveNextAsync_CancellationAfterFirstSkippedRecord_ResumesWithoutDroppingData(
        bool fixedLength, bool hasHeader)
    {
        string data = fixedLength ? "META1META2DATA1DATA2" : "META1\nMETA2\nDATA1\nDATA2\n";
        int firstChunkLength = fixedLength ? 5 : 6;
        using var cancellation = new CancellationTokenSource();
        await using var stream = new CancelOnSecondReadStream(data, firstChunkLength, cancellation);
        var options = new FixedWidthReadOptions
        {
            RecordLength = fixedLength ? 5 : null,
            SkipRows = hasHeader ? 1 : 2,
            HasHeaderRow = hasHeader,
            TrackSourceLineNumbers = true
        };

        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        // The first read supplies one complete preamble record. The next read is
        // canceled after that record has been skipped, before the second skip.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.MoveNextAsync(cancellation.Token).AsTask());
        Assert.Equal(1, stream.Cancellations);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("DATA1", reader.Current.RawRecord.ToString());
        Assert.Equal(fixedLength ? 1 : 3, reader.Current.SourceLineNumber);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("DATA2", reader.Current.RawRecord.ToString());
        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoveNextAsync_MaxSkipRowsWithHeader_DoesNotOverflowIntoVisibleData(bool fixedLength)
    {
        string data = fixedLength ? "DATA1" : "DATA1\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var options = new FixedWidthReadOptions
        {
            RecordLength = fixedLength ? 5 : null,
            SkipRows = int.MaxValue,
            HasHeaderRow = true
        };
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    private sealed class CancelOnSecondReadStream(
        string contents,
        int firstChunkLength,
        CancellationTokenSource cancellationSource)
        : MemoryStream(Encoding.UTF8.GetBytes(contents), writable: false)
    {
        private int readCalls;

        public int Cancellations { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int call = ++readCalls;
            if (call == 2)
            {
                Cancellations++;
                cancellationSource.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }

            int count = call == 1 ? Math.Min(firstChunkLength, buffer.Length) : buffer.Length;
            return ValueTask.FromResult(base.Read(buffer.Span[..count]));
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
