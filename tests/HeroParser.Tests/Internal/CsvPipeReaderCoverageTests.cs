using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives <see cref="HeroParser.Csv.ReadFromPipeReaderAsync"/> (the IAsyncEnumerable
/// CsvPipeRow path) which differs from the Sequence-based reader. Targets gaps in
/// Csv.PipeReader.cs (~189 lines, 50.7%): the row-storage packing (byte/uint16/int32
/// header sizes), CsvPipeColumn unquoting (with and without escape char), TrimFields
/// behavior, and quoted-field span access.
/// </summary>
[Trait("Category", "Unit")]
public class CsvPipeReaderCoverageTests
{
    private static async Task<List<CsvPipeRowSnapshot>> ReadAll(string csv, CsvReadOptions? options = null, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        var snapshots = new List<CsvPipeRowSnapshot>();
        await foreach (var row in HeroParser.Csv.ReadFromPipeReaderAsync(pipe, options, ct))
        {
            var cols = new List<string>(row.ColumnCount);
            for (int i = 0; i < row.ColumnCount; i++)
            {
                cols.Add(row[i].ToUnquotedString());
            }
            snapshots.Add(new CsvPipeRowSnapshot(row.RowNumber, row.ColumnCount, cols));
        }
        return snapshots;
    }

    private sealed record CsvPipeRowSnapshot(int RowNumber, int ColumnCount, List<string> Columns);

    [Fact]
    public async Task BasicRows_Counted()
    {
        var rows = await ReadAll("A,B\n1,2\n3,4\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, rows.Count); // header + 2 data
    }

    [Fact]
    public async Task RowNumber_StartsAt1()
    {
        var rows = await ReadAll("A,B\n1,2\n3,4\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, rows[0].RowNumber);
        Assert.Equal(2, rows[1].RowNumber);
        Assert.Equal(3, rows[2].RowNumber);
    }

    [Fact]
    public async Task Columns_AccessByIndex()
    {
        var rows = await ReadAll("A,B\nhello,world\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal("A", rows[0].Columns[0]);
        Assert.Equal("hello", rows[1].Columns[0]);
        Assert.Equal("world", rows[1].Columns[1]);
    }

    [Fact]
    public async Task QuotedFields_StripQuotes()
    {
        var rows = await ReadAll("A,B\n\"a,b\",x\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal("a,b", rows[1].Columns[0]);
        Assert.Equal("x", rows[1].Columns[1]);
    }

    [Fact]
    public async Task QuotedFields_EmbeddedDoubledQuote_Unescaped()
    {
        var rows = await ReadAll("A,B\n\"she said \"\"hi\"\"\",x\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal("she said \"hi\"", rows[1].Columns[0]);
    }

    [Fact]
    public async Task EscapeCharacter_UnescapesEmbedded()
    {
        var opts = new CsvReadOptions { EscapeCharacter = '\\' };
        var rows = await ReadAll("A,B\n\"a\\\"b\",x\n", opts, TestContext.Current.CancellationToken);
        Assert.Equal("a\"b", rows[1].Columns[0]);
    }

    [Fact]
    public async Task TrimFields_StripsLeadingTrailingWhitespace()
    {
        var opts = new CsvReadOptions { TrimFields = true };
        var rows = await ReadAll("A,B\n  hello  ,  world  \n", opts, TestContext.Current.CancellationToken);
        Assert.Equal("hello", rows[1].Columns[0]);
        Assert.Equal("world", rows[1].Columns[1]);
    }

    [Fact]
    public async Task TrimFields_DoesNotTrimInsideQuotes()
    {
        var opts = new CsvReadOptions { TrimFields = true };
        var rows = await ReadAll("A,B\n\"  spaces preserved  \",x\n", opts, TestContext.Current.CancellationToken);
        Assert.Equal("  spaces preserved  ", rows[1].Columns[0]);
    }

    [Fact]
    public async Task LongRow_TriggersUInt16HeaderPacking()
    {
        // Force a row > byte.MaxValue to switch from byte-packed to ushort-packed columnEnds.
        var bigField = new string('a', 300);
        var rows = await ReadAll($"A,B\n{bigField},x\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, rows.Count);
        Assert.Equal(300, rows[1].Columns[0].Length);
    }

    [Fact]
    public async Task VeryLongRow_TriggersInt32HeaderPacking()
    {
        // Force a row > ushort.MaxValue to switch from ushort to int packing.
        var bigField = new string('a', 70_000);
        var rows = await ReadAll($"A,B\n{bigField},x\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, rows.Count);
        Assert.Equal(70_000, rows[1].Columns[0].Length);
    }

    [Fact]
    public async Task EmptyFields_PreservedAsEmpty()
    {
        var rows = await ReadAll("A,B,C\n,,\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal("", rows[1].Columns[0]);
        Assert.Equal("", rows[1].Columns[1]);
        Assert.Equal("", rows[1].Columns[2]);
    }

    [Fact]
    public async Task ColumnCount_Reflects_HeaderAndDataDifferently()
    {
        var rows = await ReadAll("A,B,C\n1,2,3\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, rows[0].ColumnCount);
        Assert.Equal(3, rows[1].ColumnCount);
    }

    [Fact]
    public async Task BomStripped_FromFirstRow()
    {
        var rows = await ReadAll("﻿A,B\n1,2\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal("A", rows[0].Columns[0]); // not "﻿A"
    }

    [Fact]
    public async Task CustomDelimiter_Pipe()
    {
        var opts = new CsvReadOptions { Delimiter = '|' };
        var rows = await ReadAll("A|B\n1|2\n", opts, TestContext.Current.CancellationToken);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].ColumnCount);
    }

    [Fact]
    public async Task CustomQuote_SingleQuote()
    {
        var opts = new CsvReadOptions { Quote = '\'' };
        var rows = await ReadAll("A,B\n'hello',world\n", opts, TestContext.Current.CancellationToken);
        Assert.Equal("hello", rows[1].Columns[0]);
    }

    [Fact]
    public async Task CommentLines_Skipped()
    {
        var opts = new CsvReadOptions { CommentCharacter = '#' };
        var rows = await ReadAll("A,B\n# hidden\n1,2\n", opts, TestContext.Current.CancellationToken);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task CrLfRowSeparator_Honored()
    {
        var rows = await ReadAll("A,B\r\n1,2\r\n3,4\r\n", ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task NewlineInsideQuote_AllowedOption()
    {
        var opts = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        var rows = await ReadAll("A,B\n\"line1\nline2\",x\n", opts, TestContext.Current.CancellationToken);
        Assert.Equal(2, rows.Count);
        Assert.Contains("line1", rows[1].Columns[0]);
        Assert.Contains("line2", rows[1].Columns[0]);
    }

    [Fact]
    public async Task MultipleSegments_PipeReaderHandles()
    {
        // Force PipeReader to use small segments by writing in tiny chunks.
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 16));

        var data = "A,B\n1,foo-bar-baz-qux\n2,longer-than-segment-here\n3,short\n";
        var bytes = Encoding.UTF8.GetBytes(data);
        int written = 0;
        while (written < bytes.Length)
        {
            int chunk = Math.Min(8, bytes.Length - written);
            bytes.AsSpan(written, chunk).CopyTo(pipe.Writer.GetSpan(chunk));
            pipe.Writer.Advance(chunk);
            await pipe.Writer.FlushAsync(ct);
            written += chunk;
        }
        await pipe.Writer.CompleteAsync();

        int rowCount = 0;
        await foreach (var row in HeroParser.Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: ct))
        {
            _ = row.ColumnCount;
            rowCount++;
        }
        Assert.Equal(4, rowCount);
    }

    [Fact]
    public void ReadFromPipeReaderAsync_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HeroParser.Csv.ReadFromPipeReaderAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyPipe_NoRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe();
        await pipe.Writer.CompleteAsync();
        int count = 0;
        await foreach (var _ in HeroParser.Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: ct))
        {
            count++;
        }
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task PipeRow_Span_AccessibleDuringIteration()
    {
        var bytes = Encoding.UTF8.GetBytes("A,B\nhello,world\n");
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        int? firstColLen = null;
        await foreach (var row in HeroParser.Csv.ReadFromPipeReaderAsync(pipe,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            firstColLen ??= row[0].Span.Length;
        }
        Assert.NotNull(firstColLen);
        Assert.True(firstColLen.Value > 0);
    }

    [Fact]
    public async Task PipeColumn_ToString_AndToUnquotedString_Match_OnUnquoted()
    {
        var bytes = Encoding.UTF8.GetBytes("A,B\nplain,text\n");
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        string? toStringValue = null, toUnquotedValue = null;
        await foreach (var row in HeroParser.Csv.ReadFromPipeReaderAsync(pipe,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            if (row.RowNumber == 2)
            {
                toStringValue = row[0].ToString();
                toUnquotedValue = row[0].ToUnquotedString();
            }
        }
        Assert.Equal("plain", toStringValue);
        Assert.Equal("plain", toUnquotedValue);
    }
}
