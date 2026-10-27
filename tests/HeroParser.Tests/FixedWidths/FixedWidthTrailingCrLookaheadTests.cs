using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthTrailingCrLookaheadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoveNextAsync_FullUtf16ReadAfterSplitCrLf_DoesNotRequireUnrelatedInput(
        bool skipFirstRecord)
    {
        // A short read leaves CR at the buffer boundary; the next read contains
        // 4,096 bytes of UTF-16, starting with LF. There is no EOF or third chunk yet.
        await using var stream = new PausedUtf16Stream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        var options = new FixedWidthReadOptions
        {
            SkipRows = skipFirstRecord ? 1 : 0,
            TrackSourceLineNumbers = true
        };

        if (skipFirstRecord)
        {
            // The skip path must also preserve all rows and physical line numbers.
            // Only the non-skip case can demand a completed row before read three:
            // after skipping, the caller needs another ordinary buffer refill.
            stream.ReleaseThirdChunk();
        }

        await using var reader = FixedWidth.CreateAsyncStreamReader(
            stream, options, encoding: Encoding.Unicode, leaveOpen: true, bufferSize: 4096);

        var rows = new List<string>();
        Assert.True(await reader.MoveNextAsync(timeout.Token));
        rows.Add(reader.Current.RawRecord.ToString() + "@" + reader.Current.SourceLineNumber);

        if (!skipFirstRecord)
        {
            Assert.Equal("ONE@1", rows[0]);
            Assert.True(stream.DeliveredFullSecondChunk);
            Assert.Equal(0, stream.ThirdChunkReadCalls);
            // Releasing the previously unavailable bytes must preserve all
            // subsequent decoded records, including the 2,042-character row.
            stream.ReleaseThirdChunk();
        }

        while (await reader.MoveNextAsync(timeout.Token))
        {
            rows.Add(reader.Current.RawRecord.ToString() + "@" + reader.Current.SourceLineNumber);
        }

        string longRow = new string('X', 2042) + "@3";
        string[] expected = skipFirstRecord
            ? ["TWO@2", longRow, "END@4"]
            : ["ONE@1", "TWO@2", longRow, "END@4"];
        Assert.Equal(expected, rows);
        Assert.True(stream.DeliveredFullSecondChunk);
    }

    [Fact]
    public async Task MoveNextAsync_SkipSplitCrLfCanceledAfterFullUtf16Read_ResumesWithFreshToken()
    {
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        canceled.CancelAfter(TimeSpan.FromSeconds(12));
        await using var stream = new PausedUtf16Stream(canceled.Cancel);
        var options = new FixedWidthReadOptions { SkipRows = 1, TrackSourceLineNumbers = true };
        await using var reader = FixedWidth.CreateAsyncStreamReader(
            stream, options, encoding: Encoding.Unicode, leaveOpen: true, bufferSize: 4096);

        // Cancel as the full second byte read returns. Bounded lookahead retains
        // its LF; the old wide skip read loses the copied characters on cancellation.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            _ = await reader.MoveNextAsync(canceled.Token);
        });
        Assert.True(stream.DeliveredFullSecondChunk);
        Assert.Equal(4096, stream.SecondChunkReadSize);
        Assert.Equal(0, stream.ThirdChunkReadCalls);

        // Release before retry so an ordinary bulk refill cannot obscure the
        // skip lookahead's preservation of data across cancellation.
        stream.ReleaseThirdChunk();
        using var fresh = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fresh.CancelAfter(TimeSpan.FromSeconds(12));
        var rows = new List<string>();
        while (await reader.MoveNextAsync(fresh.Token))
        {
            rows.Add(reader.Current.RawRecord.ToString() + "@" + reader.Current.SourceLineNumber);
        }

        Assert.Equal(["TWO@2", new string('X', 2042) + "@3", "END@4"], rows);
    }

    private sealed class PausedUtf16Stream(Action? onSecondChunkDelivered = null) : Stream
    {
        private readonly byte[][] chunks =
        [
            Encoding.Unicode.GetBytes("ONE\r"),
            Encoding.Unicode.GetBytes("\nTWO\r\n" + new string('X', 2042)),
            Encoding.Unicode.GetBytes("\r\nEND\r\n")
        ];
        private readonly TaskCompletionSource<bool> thirdChunkRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int chunkIndex;
        private int chunkOffset;

        public bool DeliveredFullSecondChunk { get; private set; }

        public int SecondChunkReadSize { get; private set; }

        public int ThirdChunkReadCalls { get; private set; }

        public void ReleaseThirdChunk() => thirdChunkRelease.TrySetResult(true);

        public override async ValueTask<int> ReadAsync(
            Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (chunkIndex == 2)
            {
                ThirdChunkReadCalls++;
                await thirdChunkRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (chunkIndex == chunks.Length)
                return 0;

            byte[] chunk = chunks[chunkIndex];
            int read = Math.Min(destination.Length, chunk.Length - chunkOffset);
            chunk.AsSpan(chunkOffset, read).CopyTo(destination.Span);
            chunkOffset += read;

            if (chunkOffset == chunk.Length)
            {
                if (chunkIndex == 1)
                {
                    DeliveredFullSecondChunk = true;
                    SecondChunkReadSize = read;
                    onSecondChunkDelivered?.Invoke();
                }

                chunkIndex++;
                chunkOffset = 0;
            }

            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
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
