using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait("Category", "Unit")]
public sealed class CsvPipeCancellationResultTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BorrowedReader_PropagatesCanceledReadResultWithoutReadingAgain(bool hasBufferedRow)
    {
        byte[] input = hasBufferedRow ? Encoding.UTF8.GetBytes("must-not-emit,1\n") : [];
        var pipe = new CanceledReadPipeReader(input);
        await using (var csv = Csv.CreatePipeSequenceReader(pipe))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                _ = await csv.MoveNextAsync(TestContext.Current.CancellationToken);
            });
            Assert.Equal(1, pipe.ReadCount);
            Assert.Equal(1, pipe.AdvanceCount);
        }

        Assert.Equal(1, pipe.CompleteCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedEnumerable_PropagatesCanceledReadResultWithoutEmittingRows(bool hasBufferedRow)
    {
        byte[] input = hasBufferedRow ? Encoding.UTF8.GetBytes("must-not-emit,1\n") : [];
        var pipe = new CanceledReadPipeReader(input);
        var rows = Csv.ReadFromPipeReaderAsync(pipe, cancellationToken: TestContext.Current.CancellationToken);
        await using (var iterator = rows.GetAsyncEnumerator(TestContext.Current.CancellationToken))
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
                throw new InvalidOperationException("The parser retried a canceled PipeReader read.");
            }

            // CancelPendingRead may yield IsCanceled without canceling the caller's token.
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
