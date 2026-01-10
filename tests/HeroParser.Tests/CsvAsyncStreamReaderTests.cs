using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

public class CsvAsyncStreamReaderTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_ParsesRowsAcrossBuffers()
    {
        var csv = "a,b,c\n1,2,3\n4,5,6\n";
        await using var reader = CreateReader(csv, bufferSize: 4);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row1 = reader.Current;
        Assert.Equal(new[] { "a", "b", "c" }, row1.ToStringArray());

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row2 = reader.Current;
        Assert.Equal(new[] { "1", "2", "3" }, row2.ToStringArray());

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row3 = reader.Current;
        Assert.Equal(new[] { "4", "5", "6" }, row3.ToStringArray());

        Assert.False(await reader.MoveNextAsync(cancellationToken));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_AllowsNewlinesInsideQuotes()
    {
        var options = new CsvParserOptions { AllowNewlinesInsideQuotes = true };
        var csv = "a,\"b\nc\",d\n1,2,3";

        await using var reader = CreateReader(csv, options, bufferSize: 6);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row1 = reader.Current;
        Assert.Equal("b\nc", row1[1].UnquoteToString());

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row2 = reader.Current;
        Assert.Equal("1", row2[0].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_HandlesLargeInputWithSmallMaxRowSize()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("a,b,c\n");
        }

        var options = new CsvParserOptions { MaxRowSize = 16 };
        await using var reader = CreateReader(sb.ToString(), options, bufferSize: 8);
        var cancellationToken = TestContext.Current.CancellationToken;

        int count = 0;
        while (await reader.MoveNextAsync(cancellationToken))
        {
            count++;
        }

        Assert.Equal(1000, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_AllowsRowAtMaxRowSize()
    {
        var row = "12345678";
        var csv = $"{row}\nnext\n";
        var options = new CsvParserOptions { MaxRowSize = row.Length };
        await using var reader = CreateReader(csv, options, bufferSize: 4);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        Assert.Equal(row, reader.Current.GetString(0));

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        Assert.Equal("next", reader.Current.GetString(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_UsesBytesForMaxRowSize()
    {
        var csv = "\u20AC\n";
        var options = new CsvParserOptions { MaxRowSize = 2 };
        await using var reader = CreateReader(csv, options, bufferSize: 4);
        var cancellationToken = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<CsvException>(async () =>
            await reader.MoveNextAsync(cancellationToken));
        Assert.Equal(CsvErrorCode.ParseError, ex.ErrorCode);
    }

    private static SeparatedValues.Reading.Streaming.CsvAsyncStreamReader CreateReader(
        string csv,
        CsvParserOptions? options = null,
        int bufferSize = 16 * 1024)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var stream = new MemoryStream(bytes);
        return Csv.CreateAsyncStreamReader(stream, options, leaveOpen: false, bufferSize: bufferSize);
    }
}
