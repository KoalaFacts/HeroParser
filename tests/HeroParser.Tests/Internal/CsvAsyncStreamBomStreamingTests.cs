using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class CsvAsyncStreamBomStreamingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ShortCompleteRow_IsAvailableWithoutUnrelatedStreamBytes(
        bool enableSimd,
        bool mismatchedUtf8Prefix)
    {
        byte[] first = mismatchedUtf8Prefix ? [0xEF, (byte)'\n'] : [(byte)'A', (byte)'\n'];
        await using var stream = new PausedSecondChunkStream(first, [(byte)'B', (byte)'\n']);
        var options = new CsvReadOptions
        {
            UseSimdIfAvailable = enableSimd,
            TrackSourceLineNumbers = true
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var reader = Csv.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        // The producer has not supplied a third byte or closed its stream.
        // Reading another chunk would delay an already complete physical row.
        Assert.True(await reader.MoveNextAsync(timeout.Token));
        Assert.Equal(mismatchedUtf8Prefix ? "\uFFFD" : "A", reader.Current.GetString(0));
        Assert.Equal(1, reader.Current.LineNumber);
        Assert.Equal(1, reader.Current.SourceLineNumber);
        Assert.Equal(0, stream.SecondReadCalls);

        stream.ReleaseSecondChunk();
        Assert.True(await reader.MoveNextAsync(timeout.Token));
        Assert.Equal("B", reader.Current.GetString(0));
        Assert.Equal(2, reader.Current.LineNumber);
        Assert.Equal(2, reader.Current.SourceLineNumber);
        Assert.False(await reader.MoveNextAsync(timeout.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utf16Bom_IsRejectedAfterItsSecondByteWithoutWaitingForEof(bool bigEndian)
    {
        byte[] signature = bigEndian ? [0xFE, 0xFF] : [0xFF, 0xFE];
        await using var stream = new PausedSecondChunkStream(signature, []);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var reader = Csv.CreateAsyncStreamReader(stream, leaveOpen: true);

        var error = await Assert.ThrowsAsync<CsvException>(
            () => reader.MoveNextAsync(timeout.Token).AsTask());
        Assert.Equal(CsvErrorCode.InvalidOptions, error.ErrorCode);
        Assert.Equal(0, stream.SecondReadCalls);
    }

    [Fact]
    public async Task AmbiguousUtf8BomPrefix_WaitsThenStripsFullBom()
    {
        byte[] first = [0xEF, 0xBB];
        byte[] second = [0xBF, (byte)'Z', (byte)'\n'];
        await using var stream = new PausedSecondChunkStream(first, second);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var reader = Csv.CreateAsyncStreamReader(stream, leaveOpen: true);

        var pending = reader.MoveNextAsync(timeout.Token).AsTask();
        Assert.False(pending.IsCompleted);
        stream.ReleaseSecondChunk();

        Assert.True(await pending);
        Assert.Equal("Z", reader.Current.GetString(0));
        Assert.Equal(1, reader.Current.LineNumber);
    }

    private sealed class PausedSecondChunkStream(byte[] firstChunk, byte[] secondChunk) : Stream
    {
        private readonly TaskCompletionSource<bool> secondChunkRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int chunkIndex;
        private int chunkOffset;

        public int SecondReadCalls { get; private set; }

        public void ReleaseSecondChunk() => secondChunkRelease.TrySetResult(true);

        public override async ValueTask<int> ReadAsync(
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (chunkIndex == 1)
            {
                SecondReadCalls++;
                await secondChunkRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (chunkIndex >= 2)
                return 0;

            byte[] chunk = chunkIndex == 0 ? firstChunk : secondChunk;
            int read = Math.Min(destination.Length, chunk.Length - chunkOffset);
            chunk.AsSpan(chunkOffset, read).CopyTo(destination.Span);
            chunkOffset += read;

            if (chunkOffset == chunk.Length)
            {
                chunkIndex++;
                chunkOffset = 0;
            }

            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
