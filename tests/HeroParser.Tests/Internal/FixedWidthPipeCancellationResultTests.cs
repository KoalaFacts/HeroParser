using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthPipeCancellationResultTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OwnedReader_CanceledReadResult_DoesNotReadAgainOrEmitData(
        bool fixedLength, bool hasBufferedRow)
    {
        byte[] bytes = hasBufferedRow
            ? Encoding.UTF8.GetBytes(fixedLength ? "kept" : "kept\n")
            : [];
        var pipe = new CanceledReadPipeReader(bytes);
        var options = new FixedWidthReadOptions { RecordLength = fixedLength ? 4 : null };

        var rows = FixedWidth.ReadFromPipeReaderAsync(
            pipe, options, TestContext.Current.CancellationToken);
        await using (var iterator = rows.GetAsyncEnumerator())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                _ = await iterator.MoveNextAsync();
            });
            Assert.Equal(1, pipe.ReadCount);
            Assert.Equal(1, pipe.AdvanceCount);
        }

        Assert.Equal(1, pipe.CompleteCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BorrowedReader_CancelPendingRead_PreservesBufferedRecordForRetry(bool fixedLength)
    {
        var pipe = new Pipe();
        byte[] bytes = Encoding.UTF8.GetBytes(fixedLength ? "kept" : "kept\n");
        await pipe.Writer.WriteAsync(bytes, TestContext.Current.CancellationToken);
        pipe.Reader.CancelPendingRead();

        var options = new FixedWidthReadOptions { RecordLength = fixedLength ? 4 : null };
        await using (var reader = new FixedWidthPipeSequenceReader(pipe.Reader, options))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                _ = await reader.MoveNextAsync(TestContext.Current.CancellationToken);
            });

            // The writer is still open. A second read must see the same bytes without
            // waiting for another write or silently losing the canceled record.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            Assert.True(await reader.MoveNextAsync(timeout.Token));

            var row = reader.Current.ToOwnedRow();
            Assert.Equal(1, row.RecordNumber);
            Assert.Equal("kept", row.ToDecodedString());
        }

        await pipe.Writer.CompleteAsync();
    }

    private sealed class CanceledReadPipeReader(byte[] bytes) : PipeReader
    {
        public int ReadCount { get; private set; }
        public int AdvanceCount { get; private set; }
        public int CompleteCount { get; private set; }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (ReadCount != 1)
            {
                throw new InvalidOperationException("The reader retried a canceled PipeReader read.");
            }

            // CancelPendingRead sets IsCanceled without canceling the caller's token.
            return ValueTask.FromResult(new ReadResult(new ReadOnlySequence<byte>(bytes), true, false));
        }

        public override void AdvanceTo(SequencePosition consumed) => AdvanceCount++;
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => AdvanceCount++;
        public override void CancelPendingRead() { }
        public override void Complete(Exception? exception = null) => CompleteCount++;

        public override bool TryRead(out ReadResult result)
        {
            result = default;
            return false;
        }
    }
}
