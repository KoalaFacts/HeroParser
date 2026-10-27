using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class PipeSequencePreCanceledCurrentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FixedWidth_PreCanceledMoveInvalidatesCurrentWithoutLosingBufferedRow(bool fixedLength)
    {
        var pipe = new Pipe();
        byte[] input = Encoding.UTF8.GetBytes(fixedLength ? "ONETWO" : "ONE\nTWO\n");
        await pipe.Writer.WriteAsync(input, TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();

        var options = new FixedWidthReadOptions
        {
            RecordLength = fixedLength ? 3 : null,
            TrackSourceLineNumbers = true
        };
        await using var reader = new FixedWidthPipeSequenceReader(pipe.Reader, options);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        var first = reader.Current.ToOwnedRow();
        Assert.Equal("ONE", first.ToDecodedString());
        Assert.Equal(1, first.RecordNumber);
        Assert.Equal(1, first.SourceLineNumber);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.MoveNextAsync(canceled.Token).AsTask());

        // Current is borrowed: the next MoveNextAsync call invalidates its view
        // even if the attempted advance was canceled before reading new bytes.
        Assert.Throws<InvalidOperationException>(() => Assert.Equal(1, reader.Current.RecordNumber));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        var second = reader.Current.ToOwnedRow();
        Assert.Equal("TWO", second.ToDecodedString());
        Assert.Equal(2, second.RecordNumber);
        Assert.Equal(fixedLength ? 1 : 2, second.SourceLineNumber);
        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Csv_PreCanceledMoveInvalidatesCurrentWithoutLosingBufferedBatchRow()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(
            Encoding.UTF8.GetBytes("one,\"a,b\"\ntwo,3\n"),
            TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();

        await using var reader = Csv.CreatePipeSequenceReader(pipe.Reader);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, reader.Current.RowNumber);
        Assert.Equal(2, reader.Current.ColumnCount);
        Assert.Equal("one", reader.Current[0].ToUnquotedString());
        Assert.Equal("a,b", reader.Current[1].ToUnquotedString());

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.MoveNextAsync(canceled.Token).AsTask());

        Assert.Throws<InvalidOperationException>(() => Assert.Equal(1, reader.Current.RowNumber));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, reader.Current.RowNumber);
        Assert.Equal(2, reader.Current.ColumnCount);
        Assert.Equal("two", reader.Current[0].ToUnquotedString());
        Assert.Equal("3", reader.Current[1].ToUnquotedString());
        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }
}
