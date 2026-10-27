using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class CsvAsyncStreamCurrentLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_IsValidOnlyAfterSuccessfulMoveAndBeforeEof(bool enableSimd)
    {
        var options = new CsvReadOptions
        {
            UseSimdIfAvailable = enableSimd,
            TrackSourceLineNumbers = true
        };
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ONE,1\nTWO,2\n"), writable: false);
        await using var reader = Csv.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        Assert.Throws<InvalidOperationException>(() => Assert.Equal(0, reader.Current.LineNumber));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, reader.Current.ColumnCount);
        Assert.Equal("ONE", reader.Current.GetString(0));
        Assert.Equal("1", reader.Current.GetString(1));
        Assert.Equal(1, reader.Current.LineNumber);
        Assert.Equal(1, reader.Current.SourceLineNumber);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, reader.Current.ColumnCount);
        Assert.Equal("TWO", reader.Current.GetString(0));
        Assert.Equal("2", reader.Current.GetString(1));
        Assert.Equal(2, reader.Current.LineNumber);
        Assert.Equal(2, reader.Current.SourceLineNumber);

        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => Assert.Equal(2, reader.Current.LineNumber));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCanceledMove_InvalidatesCurrentAndRetainsBufferedNextRow(bool enableSimd)
    {
        var options = new CsvReadOptions { UseSimdIfAvailable = enableSimd };
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ONE,1\nTWO,2\n"), writable: false);
        await using var reader = Csv.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ONE", reader.Current.GetString(0));

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.MoveNextAsync(canceled.Token).AsTask());
        Assert.Throws<InvalidOperationException>(() => Assert.Equal(1, reader.Current.LineNumber));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("TWO", reader.Current.GetString(0));
        Assert.Equal("2", reader.Current.GetString(1));
        Assert.Equal(2, reader.Current.LineNumber);
        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_RejectsCurrentAndFurtherReads(bool enableSimd)
    {
        var options = new CsvReadOptions { UseSimdIfAvailable = enableSimd };
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ONE,1\n"), writable: false);
        await using var reader = Csv.CreateAsyncStreamReader(stream, options, leaveOpen: true);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ONE", reader.Current.GetString(0));

        await reader.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => Assert.Equal(1, reader.Current.LineNumber));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => reader.MoveNextAsync(TestContext.Current.CancellationToken).AsTask());
    }
}
