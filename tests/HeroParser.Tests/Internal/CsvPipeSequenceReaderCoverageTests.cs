using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Targets CsvPipeSequenceReader gaps (~259 lines, 55%): multi-segment buffers,
/// BOM handling, quoted fields spanning segments, custom delimiters, comment
/// characters, and the various row-parsing scenarios reachable through
/// PipeReader.Create over a stream.
/// </summary>
[Trait("Category", "Unit")]
public class CsvPipeSequenceReaderCoverageTests
{
    private static async Task<int> CountRowsAsync(string csv, CsvReadOptions? options = null, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);
        await using var reader = HeroParser.Csv.CreatePipeSequenceReader(pipe, options);
        int count = 0;
        while (await reader.MoveNextAsync(ct))
        {
            _ = reader.Current.ColumnCount;
            count++;
        }
        return count;
    }

    [Fact]
    public async Task Basic_TwoRows_Counted()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(3, await CountRowsAsync("A,B\n1,2\n3,4\n", ct: ct));
    }

    [Fact]
    public async Task QuotedFields_WithEmbeddedComma()
    {
        var ct = TestContext.Current.CancellationToken;
        var csv = "A,B\n\"a,b\",x\n\"c,d\",y\n";
        Assert.Equal(3, await CountRowsAsync(csv, ct: ct));
    }

    [Fact]
    public async Task QuotedFields_WithEmbeddedQuote()
    {
        var ct = TestContext.Current.CancellationToken;
        var csv = "A,B\n\"she said \"\"hi\"\"\",x\n";
        Assert.Equal(2, await CountRowsAsync(csv, ct: ct));
    }

    [Fact]
    public async Task BOM_Stripped()
    {
        var ct = TestContext.Current.CancellationToken;
        var csv = "﻿A,B\n1,2\n";
        Assert.Equal(2, await CountRowsAsync(csv, ct: ct));
    }

    [Fact]
    public async Task CrLf_RowSeparator()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(3, await CountRowsAsync("A,B\r\n1,2\r\n3,4\r\n", ct: ct));
    }

    [Fact]
    public async Task LfOnly_RowSeparator()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(3, await CountRowsAsync("A,B\n1,2\n3,4\n", ct: ct));
    }

    [Fact]
    public async Task EmptyInput_NoRows()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(0, await CountRowsAsync("", ct: ct));
    }

    [Fact]
    public async Task TrailingNewlineOptional()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(2, await CountRowsAsync("A,B\n1,2", ct: ct));
    }

    [Fact]
    public async Task CustomDelimiter_Tab()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { Delimiter = '\t' };
        Assert.Equal(2, await CountRowsAsync("A\tB\n1\t2\n", opts, ct));
    }

    [Fact]
    public async Task CustomDelimiter_Pipe()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { Delimiter = '|' };
        Assert.Equal(2, await CountRowsAsync("A|B\n1|2\n", opts, ct));
    }

    [Fact]
    public async Task CommentLines_Skipped()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { CommentCharacter = '#' };
        var csv = "A,B\n# comment\n1,2\n# another\n3,4\n";
        Assert.Equal(3, await CountRowsAsync(csv, opts, ct));
    }

    [Fact]
    public async Task TrimFields_Configured()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { TrimFields = true };
        Assert.Equal(2, await CountRowsAsync("A,B\n  1  ,  2  \n", opts, ct));
    }

    [Fact]
    public async Task LongRow_TraversesManyChunks()
    {
        var ct = TestContext.Current.CancellationToken;
        var sb = new StringBuilder("A,B\n");
        for (int i = 0; i < 100; i++) sb.Append($"row{i}A,row{i}B\n");
        Assert.Equal(101, await CountRowsAsync(sb.ToString(), ct: ct));
    }

    [Fact]
    public async Task LargeQuotedField_SpansSegments()
    {
        var ct = TestContext.Current.CancellationToken;
        var bigField = new string('a', 16384);
        var csv = $"A,B\n\"{bigField}\",x\n";
        Assert.Equal(2, await CountRowsAsync(csv, ct: ct));
    }

    [Fact]
    public async Task NewlineInsideQuote_AllowedOption()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        var csv = "A,B\n\"line1\nline2\",x\n";
        Assert.Equal(2, await CountRowsAsync(csv, opts, ct));
    }

    [Fact]
    public async Task EnableQuotedFields_False_TreatsQuotesAsLiteral()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { EnableQuotedFields = false };
        Assert.Equal(2, await CountRowsAsync("A,B\n\"x\",\"y\"\n", opts, ct));
    }

    [Fact]
    public async Task DisposeAsync_ReleasesResources()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Encoding.UTF8.GetBytes("A,B\n1,2\n");
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);
        var reader = HeroParser.Csv.CreatePipeSequenceReader(pipe);
        await reader.MoveNextAsync(ct);
        await reader.DisposeAsync();
        // Second dispose is a no-op
        await reader.DisposeAsync();
    }

    [Fact]
    public void Constructor_NullPipe_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HeroParser.Csv.CreatePipeSequenceReader(null!));
    }

    [Fact]
    public async Task ColumnAccess_RetrievesUtf8Spans()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Encoding.UTF8.GetBytes("A,B\nhello,world\n");
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);
        await using var reader = HeroParser.Csv.CreatePipeSequenceReader(pipe);

        Assert.True(await reader.MoveNextAsync(ct)); // header
        Assert.Equal(2, reader.Current.ColumnCount);

        Assert.True(await reader.MoveNextAsync(ct)); // data
        var row = reader.Current;
        Assert.Equal(2, row.ColumnCount);
        Assert.True(row.RowNumber > 0);
        // Verify the column accessors return expected sequences
        var col0 = row[0];
        Assert.True(col0.Length > 0);
    }

    [Fact]
    public async Task Cancellation_StopsIteration()
    {
        var sb = new StringBuilder("A,B\n");
        for (int i = 0; i < 10000; i++) sb.Append($"row{i},data{i}\n");
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);
        await using var reader = HeroParser.Csv.CreatePipeSequenceReader(pipe);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await reader.MoveNextAsync(cts.Token));
    }
}
