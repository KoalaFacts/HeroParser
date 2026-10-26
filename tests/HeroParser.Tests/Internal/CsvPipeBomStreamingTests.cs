using System.IO.Pipelines;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class CsvPipeBomStreamingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ShortNonBomRow_IsAvailableBeforeWriterCompletes(bool owned, bool mismatchedBomPrefix)
    {
        // A two-byte row is complete even though the writer stays open. A matching
        // first byte (EF) must not delay it when the second byte rules out EF BB BF.
        byte[] input = mismatchedBomPrefix ? [0xEF, (byte)'\n'] : [(byte)'A', (byte)'\n'];
        byte expected = mismatchedBomPrefix ? (byte)0xEF : (byte)'A';
        var pipe = new Pipe();
        var options = new CsvReadOptions { TrackSourceLineNumbers = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await pipe.Writer.WriteAsync(input, timeout.Token);

        if (owned)
        {
            await using (var enumerator = Csv.ReadFromPipeReaderAsync(pipe.Reader, options, timeout.Token)
                .GetAsyncEnumerator(timeout.Token))
            {
                Assert.True(await enumerator.MoveNextAsync());
                Assert.Equal(1, enumerator.Current.ColumnCount);
                Assert.Equal(expected, enumerator.Current[0].Span[0]);
                Assert.Equal(1, enumerator.Current.RowNumber);
                Assert.Equal(1, enumerator.Current.SourceLineNumber);
            }
        }
        else
        {
            await using (var reader = Csv.CreatePipeSequenceReader(pipe.Reader, options))
            {
                Assert.True(await reader.MoveNextAsync(timeout.Token));
                Assert.Equal(1, reader.Current.ColumnCount);
                Assert.Equal(expected, reader.Current[0].Span[0]);
                Assert.Equal(1, reader.Current.RowNumber);
                Assert.Equal(1, reader.Current.SourceLineNumber);
            }
        }

        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task RealBomSplitAcrossWrites_IsStillStripped()
    {
        var pipe = new Pipe();
        var options = new CsvReadOptions { TrackSourceLineNumbers = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        byte[] first = [0xEF];
        await pipe.Writer.WriteAsync(first, timeout.Token);

        await using (var reader = Csv.CreatePipeSequenceReader(pipe.Reader, options))
        {
            var next = reader.MoveNextAsync(timeout.Token).AsTask();
            Assert.False(next.IsCompleted);

            byte[] second = [0xBB];
            await pipe.Writer.WriteAsync(second, timeout.Token);
            Assert.False(next.IsCompleted);

            byte[] final = [0xBF, (byte)'Z', (byte)'\n'];
            await pipe.Writer.WriteAsync(final, timeout.Token);
            Assert.True(await next);
            Assert.Equal(1, reader.Current.ColumnCount);
            Assert.Equal("Z", reader.Current[0].ToUnquotedString());
            Assert.Equal(1, reader.Current.RowNumber);
            Assert.Equal(1, reader.Current.SourceLineNumber);
        }

        await pipe.Writer.CompleteAsync();
    }
}
