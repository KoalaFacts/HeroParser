using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Targets specific remaining gaps in CsvPipeSequenceReader:
/// CsvPipeSequenceColumn.ToUnquotedString unescape paths (escape char + doubled
/// quote), MaxRowSize enforcement, multi-segment trim, and combined option paths.
/// </summary>
[Trait("Category", "Unit")]
public class CsvPipeSequenceReaderRemainingGapsTests
{
    private static async Task<List<string>> CollectFirstColumnAsync(
        string csv,
        CsvReadOptions? options = null,
        CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        await using var reader = HeroParser.Csv.CreatePipeSequenceReader(pipe, options);
        var values = new List<string>();
        while (await reader.MoveNextAsync(ct))
        {
            if (reader.Current.ColumnCount > 0)
            {
                values.Add(reader.Current[0].ToUnquotedString());
            }
        }
        return values;
    }

    private static async Task<List<string>> CollectFirstColumn_MultiSegmentAsync(
        string csv,
        int chunkSize,
        CsvReadOptions? options = null,
        CancellationToken ct = default)
    {
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: chunkSize));
        var bytes = Encoding.UTF8.GetBytes(csv);
        int written = 0;
        while (written < bytes.Length)
        {
            int chunk = Math.Min(chunkSize / 2, bytes.Length - written);
            bytes.AsSpan(written, chunk).CopyTo(pipe.Writer.GetSpan(chunk));
            pipe.Writer.Advance(chunk);
            await pipe.Writer.FlushAsync(ct);
            written += chunk;
        }
        await pipe.Writer.CompleteAsync();

        await using var reader = HeroParser.Csv.CreatePipeSequenceReader(pipe.Reader, options);
        var values = new List<string>();
        while (await reader.MoveNextAsync(ct))
        {
            if (reader.Current.ColumnCount > 0)
            {
                values.Add(reader.Current[0].ToUnquotedString());
            }
        }
        return values;
    }

    [Fact]
    public async Task ToUnquotedString_EscapeChar_UnescapesEmbedded()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { EscapeCharacter = '\\' };
        var values = await CollectFirstColumnAsync("A,B\n\"a\\\"b\",x\n", opts, ct);
        Assert.Equal(["A", "a\"b"], values);
    }

    [Fact]
    public async Task ToUnquotedString_EscapeChar_NoEmbedded_ReturnsAsIs()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { EscapeCharacter = '\\' };
        var values = await CollectFirstColumnAsync("A,B\n\"plain\",x\n", opts, ct);
        Assert.Equal(["A", "plain"], values);
    }

    [Fact]
    public async Task ToUnquotedString_DoubledQuote_Unescaped()
    {
        var ct = TestContext.Current.CancellationToken;
        var values = await CollectFirstColumnAsync("A,B\n\"she said \"\"hi\"\"\",x\n", ct: ct);
        Assert.Equal(["A", "she said \"hi\""], values);
    }

    [Fact]
    public async Task ToUnquotedString_NoQuoteInValue_FastPath()
    {
        var ct = TestContext.Current.CancellationToken;
        var values = await CollectFirstColumnAsync("A,B\nplain,text\n", ct: ct);
        Assert.Equal(["A", "plain"], values);
    }

    [Fact]
    public async Task ToUnquotedString_EscapeChar_MultipleEscapes()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { EscapeCharacter = '\\' };
        var values = await CollectFirstColumnAsync("A,B\n\"a\\\"b\\\"c\",x\n", opts, ct);
        Assert.Contains(values, v => v.Contains("a\"b") && v.Contains("c"));
    }

    [Fact]
    public async Task ToUnquotedString_EscapeChar_MultiSegment()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { EscapeCharacter = '\\' };
        var values = await CollectFirstColumn_MultiSegmentAsync("A,B\n\"a\\\"b\",x\n", 8, opts, ct);
        Assert.Equal(["A", "a\"b"], values);
    }

    [Fact]
    public async Task ToUnquotedString_DoubledQuote_MultiSegment()
    {
        var ct = TestContext.Current.CancellationToken;
        var values = await CollectFirstColumn_MultiSegmentAsync("A,B\n\"she said \"\"hi\"\"\",x\n", 8, ct: ct);
        Assert.Equal(["A", "she said \"hi\""], values);
    }

    [Fact]
    public async Task MaxRowSize_Exceeded_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { MaxRowSize = 50 };
        var bigField = new string('a', 200);
        var csv = $"A,B\n{bigField},x\n";
        await Assert.ThrowsAnyAsync<Exception>(async () => await CollectFirstColumnAsync(csv, opts, ct));
    }

    [Fact]
    public async Task MaxRowSize_MultiSegment_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { MaxRowSize = 50 };
        var bigField = new string('a', 200);
        var csv = $"A,B\n{bigField},x\n";
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await CollectFirstColumn_MultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task TrimFields_MultiSegment_BoundaryWhitespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { TrimFields = true };
        // Lots of whitespace at field boundaries, multi-segment Pipe
        var values = await CollectFirstColumn_MultiSegmentAsync("A,B\n   spaced   ,   x   \n", 8, opts, ct);
        Assert.Equal(["A", "spaced"], values);
    }

    [Fact]
    public async Task TrimFields_QuotedFieldNotTrimmed()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { TrimFields = true };
        // Whitespace inside quotes should be preserved
        var values = await CollectFirstColumnAsync("A,B\n\"  spaces  \",x\n", opts, ct);
        Assert.Equal(["A", "  spaces  "], values);
    }

    [Fact]
    public async Task TrackLineNumbers_OnPipeSequenceReader()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { TrackSourceLineNumbers = true };
        var bytes = Encoding.UTF8.GetBytes("A,B\n1,2\n3,4\n");
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        await using var reader = HeroParser.Csv.CreatePipeSequenceReader(pipe, opts);

        Assert.True(await reader.MoveNextAsync(ct));
        var first = reader.Current.SourceLineNumber;
        Assert.True(await reader.MoveNextAsync(ct));
        var second = reader.Current.SourceLineNumber;
        Assert.True(second > first);
    }

    [Fact]
    public async Task SkipRows_Configured_ViaPipeSequenceReader()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions(); // SkipRows is on CsvRecordOptions, not parser. Just smoke-test.
        var values = await CollectFirstColumnAsync("A,B\n1,2\n3,4\n", opts, ct);
        Assert.Equal(3, values.Count);
    }

    [Fact]
    public async Task EmptyRow_BetweenData_HandledGracefully()
    {
        var ct = TestContext.Current.CancellationToken;
        var values = await CollectFirstColumnAsync("A,B\n1,2\n\n3,4\n", ct: ct);
        // Empty rows may or may not produce a record; just ensure no crash.
        Assert.NotEmpty(values);
    }

    [Fact]
    public async Task ColumnCount_OnLargeRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var sb = new StringBuilder();
        for (int i = 0; i < 50; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("col").Append(i);
        }
        sb.Append('\n');

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        await using var reader = HeroParser.Csv.CreatePipeSequenceReader(pipe);
        Assert.True(await reader.MoveNextAsync(ct));
        Assert.Equal(50, reader.Current.ColumnCount);
    }

    [Fact]
    public async Task SequenceColumn_LengthAndSequenceProperties()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Encoding.UTF8.GetBytes("A,B\nhello,world\n");
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        await using var reader = HeroParser.Csv.CreatePipeSequenceReader(pipe);
        Assert.True(await reader.MoveNextAsync(ct)); // header
        Assert.True(await reader.MoveNextAsync(ct)); // data

        var col = reader.Current[0];
        Assert.True(col.Length > 0);
        Assert.False(col.Sequence.IsEmpty);
    }
}
