using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the multi-segment <see cref="ReadOnlySequence{byte}"/> path inside
/// <c>Csv.PipeSequenceReader.ParsePipeSequenceRow</c> by writing CSV bytes through a
/// small-segment <see cref="Pipe"/>. Targets the deep gap (~184 lines) in
/// Csv.PipeSequenceReader.cs.
/// </summary>
[Trait("Category", "Unit")]
public class CsvPipeSequenceMultiSegmentTests
{
    private static async Task<int> CountRowsMultiSegmentAsync(
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
        int count = 0;
        while (await reader.MoveNextAsync(ct))
        {
            _ = reader.Current.ColumnCount;
            count++;
        }
        return count;
    }

    [Fact]
    public async Task MultiSegment_BasicRows()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(3, await CountRowsMultiSegmentAsync("A,B\nfoo,bar\nbaz,qux\n", 8, ct: ct));
    }

    [Fact]
    public async Task MultiSegment_QuotedFieldWithEmbeddedComma()
    {
        var ct = TestContext.Current.CancellationToken;
        var csv = "A,B\n\"this,has,commas\",x\n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 8, ct: ct));
    }

    [Fact]
    public async Task MultiSegment_QuotedField_EmbeddedDoubledQuote()
    {
        var ct = TestContext.Current.CancellationToken;
        var csv = "A,B\n\"she said \"\"hi\"\"\",x\n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 8, ct: ct));
    }

    [Fact]
    public async Task MultiSegment_EscapeCharacter()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { EscapeCharacter = '\\' };
        var csv = "A,B\n\"a\\\"b\",x\n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task MultiSegment_NewlineInsideQuotes_Allowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        var csv = "A,B\n\"line1\nline2\nline3\",x\n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task MultiSegment_NewlineInsideQuotes_Rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { AllowNewlinesInsideQuotes = false };
        var csv = "A,B\n\"line1\nline2\",x\n";
        await Assert.ThrowsAsync<CsvException>(() => CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task MultiSegment_CommentLine_Skipped()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { CommentCharacter = '#' };
        var csv = "A,B\n# this is a comment line\nfoo,bar\n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task MultiSegment_LeadingWhitespaceBeforeComment_Skipped()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { CommentCharacter = '#' };
        var csv = "A,B\n  # indented comment\nfoo,bar\n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task MultiSegment_CrLf_RowSeparator()
    {
        var ct = TestContext.Current.CancellationToken;
        var csv = "A,B\r\n1,2\r\n3,4\r\n";
        Assert.Equal(3, await CountRowsMultiSegmentAsync(csv, 8, ct: ct));
    }

    [Fact]
    public async Task MultiSegment_CustomDelimiter()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { Delimiter = '|' };
        var csv = "A|B\nfoo|bar\nbaz|qux\n";
        Assert.Equal(3, await CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task MultiSegment_TrimFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { TrimFields = true };
        var csv = "A,B\n  foo  ,  bar  \n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task MultiSegment_VeryLargeQuotedField()
    {
        var ct = TestContext.Current.CancellationToken;
        var bigField = new string('a', 32 * 1024);
        var csv = $"A,B\n\"{bigField}\",x\n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 64, ct: ct));
    }

    [Fact]
    public async Task MultiSegment_MaxRowSize_Throws_WhenExceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { MaxRowSize = 100 };
        var bigField = new string('a', 200);
        var csv = $"A,B\n{bigField},x\n";
        await Assert.ThrowsAnyAsync<Exception>(() => CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task MultiSegment_QuotedFieldAtEndOfFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var csv = "A,B\n1,\"last\"";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 8, ct: ct));
    }

    [Fact]
    public async Task MultiSegment_EmptyCommentLines_Mixed()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { CommentCharacter = '#' };
        var csv = "A,B\n#c1\nfoo,bar\n#c2\nbaz,qux\n#c3\n";
        Assert.Equal(3, await CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }

    [Fact]
    public async Task MultiSegment_BomStripped()
    {
        var ct = TestContext.Current.CancellationToken;
        var csv = "﻿A,B\n1,2\n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 4, ct: ct));
    }

    [Fact]
    public async Task MultiSegment_EnableQuotedFields_False_TreatsQuoteAsLiteral()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new CsvReadOptions { EnableQuotedFields = false };
        var csv = "A,B\n\"x\",\"y\"\n";
        Assert.Equal(2, await CountRowsMultiSegmentAsync(csv, 8, opts, ct));
    }
}
