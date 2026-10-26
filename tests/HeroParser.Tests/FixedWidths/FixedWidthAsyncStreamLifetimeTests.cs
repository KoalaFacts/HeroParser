using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthAsyncStreamLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_RequiresSuccessfulMoveNextAndDoesNotExposeTheLastRowAfterEof(bool fixedLength)
    {
        string input = fixedLength ? "ONETWO" : "ONE\nTWO\n";
        var options = new FixedWidthReadOptions
        {
            RecordLength = fixedLength ? 3 : null,
            TrackSourceLineNumbers = true
        };

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input), writable: false);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => Assert.Equal(0, reader.Current.RecordNumber));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ONE", reader.Current.RawRecord.ToString());
        Assert.Equal(1, reader.Current.RecordNumber);
        Assert.Equal(1, reader.Current.SourceLineNumber);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("TWO", reader.Current.RawRecord.ToString());
        Assert.Equal(2, reader.Current.RecordNumber);
        Assert.Equal(fixedLength ? 1 : 2, reader.Current.SourceLineNumber);

        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => Assert.Equal(2, reader.Current.RecordNumber));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_IsInvalidatedWhenNextMoveIsCancelled(bool fixedLength)
    {
        string input = fixedLength ? "ONETWO" : "ONE\nTWO\n";
        var options = new FixedWidthReadOptions { RecordLength = fixedLength ? 3 : null };
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input), writable: false);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ONE", reader.Current.RawRecord.ToString());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.MoveNextAsync(cancellation.Token).AsTask());
        Assert.Throws<InvalidOperationException>(() => Assert.Equal(1, reader.Current.RecordNumber));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("TWO", reader.Current.RawRecord.ToString());
        Assert.Equal(2, reader.Current.RecordNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_RejectsCurrentAndFurtherReads(bool fixedLength)
    {
        string input = fixedLength ? "ONETWO" : "ONE\nTWO\n";
        var options = new FixedWidthReadOptions { RecordLength = fixedLength ? 3 : null };
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input), writable: false);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ONE", reader.Current.RawRecord.ToString());

        await reader.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => Assert.Equal(1, reader.Current.RecordNumber));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => reader.MoveNextAsync(TestContext.Current.CancellationToken).AsTask());
    }
}
