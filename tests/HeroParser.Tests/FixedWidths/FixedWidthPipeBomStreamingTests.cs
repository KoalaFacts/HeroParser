using System.IO.Pipelines;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthPipeBomStreamingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonBomPrefix_YieldsCompleteShortRecordWhileWriterRemainsOpen(bool fixedLength)
    {
        var pipe = new Pipe();
        byte[] data = fixedLength ? [(byte)'A'] : [(byte)'A', (byte)'\n'];
        var options = new FixedWidthReadOptions { RecordLength = fixedLength ? 1 : null };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await pipe.Writer.WriteAsync(data, timeout.Token);

        await using (var enumerator = FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, timeout.Token)
            .GetAsyncEnumerator(timeout.Token))
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("A", enumerator.Current.ToDecodedString());
            Assert.Equal(1, enumerator.Current.RecordNumber);
        }

        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task MismatchedSecondBomByte_DoesNotDelayCompleteRecord()
    {
        var pipe = new Pipe();
        byte[] data = [0xEF, (byte)'A'];
        var options = new FixedWidthReadOptions { RecordLength = 2 };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await pipe.Writer.WriteAsync(data, timeout.Token);

        await using (var enumerator = FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, timeout.Token)
            .GetAsyncEnumerator(timeout.Token))
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.True(enumerator.Current.RawRecord.SequenceEqual(data));
        }

        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task RealBomAcrossWrites_IsStillStrippedBeforeFirstShortRecord()
    {
        var pipe = new Pipe();
        var options = new FixedWidthReadOptions { RecordLength = 1 };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        byte[] first = [0xEF];
        await pipe.Writer.WriteAsync(first, timeout.Token);

        await using (var enumerator = FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, timeout.Token)
            .GetAsyncEnumerator(timeout.Token))
        {
            var next = enumerator.MoveNextAsync().AsTask();
            Assert.False(next.IsCompleted);

            byte[] second = [0xBB];
            await pipe.Writer.WriteAsync(second, timeout.Token);
            Assert.False(next.IsCompleted);

            byte[] final = [0xBF, (byte)'Z'];
            await pipe.Writer.WriteAsync(final, timeout.Token);
            Assert.True(await next);
            Assert.Equal("Z", enumerator.Current.ToDecodedString());
        }

        await pipe.Writer.CompleteAsync();
    }
}
