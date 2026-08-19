using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the PipeReader-based CSV readers with input delivered a few bytes at a time.
///
/// The pipe readers exist for sockets and HTTP bodies, where a row routinely arrives split
/// across reads. Every existing test hands them a MemoryStream, which delivers the whole
/// payload in one buffer, so the "need more data" branches — the ones that decide whether a
/// half-seen quote, escape, comment or BOM is complete — had never run. A stream that
/// returns a few bytes per read makes them the normal case instead of the unreachable one.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class CsvPipeReaderChunkedTests
{
    /// <summary>A read-only stream that hands out at most <c>chunkSize</c> bytes per read.</summary>
    private sealed class ChunkedStream(byte[] data, int chunkSize) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int take = Math.Min(Math.Min(chunkSize, count), data.Length - position);
            Array.Copy(data, position, buffer, offset, take);
            position += take;
            return take;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static PipeReader Chunked(string csv, int chunkSize = 3)
        => PipeReader.Create(new ChunkedStream(Encoding.UTF8.GetBytes(csv), chunkSize), new StreamPipeReaderOptions(bufferSize: 16, minimumReadSize: 1));

    private static PipeReader Chunked(byte[] csv, int chunkSize = 3)
        => PipeReader.Create(new ChunkedStream(csv, chunkSize), new StreamPipeReaderOptions(bufferSize: 16, minimumReadSize: 1));

    private static async Task<List<string[]>> ReadRowsAsync(PipeReader reader, CsvReadOptions? options = null)
    {
        var rows = new List<string[]>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(reader, options, TestContext.Current.CancellationToken))
        {
            var columns = new string[row.ColumnCount];
            for (int i = 0; i < row.ColumnCount; i++)
            {
                columns[i] = row[i].ToString();
            }
            rows.Add(columns);
        }
        return rows;
    }

    // ---- row enumerable --------------------------------------------------------

    [Fact]
    public async Task Rows_SplitAcrossReads_AreReassembled()
    {
        var rows = await ReadRowsAsync(Chunked("a,b,c\n1,2,3\n4,5,6\n"));

        Assert.Equal(3, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["4", "5", "6"], rows[2]);
    }

    [Fact]
    public async Task QuotedFieldSplitAcrossReads_KeepsItsContents()
    {
        var rows = await ReadRowsAsync(Chunked("name,note\n\"Smith, John\",\"said \"\"hi\"\"\"\n"));

        // Columns come back unquoted, with doubled quotes collapsed.
        Assert.Equal(2, rows.Count);
        Assert.Equal("Smith, John", rows[1][0]);
        Assert.Equal("said \"hi\"", rows[1][1]);
    }

    [Fact]
    public async Task NewlineInsideQuotes_IsPartOfTheField()
    {
        var options = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        var rows = await ReadRowsAsync(Chunked("a\n\"line1\nline2\"\n"), options);

        Assert.Equal(2, rows.Count);
        Assert.Contains("line1\nline2", rows[1][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommentLines_AreRecognisedWhenAConfiguredCharacterStartsThem()
    {
        var options = new CsvReadOptions { CommentCharacter = '#' };
        var rows = await ReadRowsAsync(Chunked("# leading\na,b\n#trailing\n1,2\n"), options);

        // Comment rows come through with no columns, so the data rows are still identifiable.
        Assert.Equal(["a", "b"], rows.Single(r => r.Length == 2 && r[0] == "a"));
        Assert.Equal(["1", "2"], rows.Single(r => r.Length == 2 && r[0] == "1"));
    }

    [Fact]
    public async Task CommentLine_IndentedByWhitespace_IsStillAComment()
    {
        // The comment character may follow spaces or tabs at the start of the line.
        var options = new CsvReadOptions { CommentCharacter = '#' };
        var rows = await ReadRowsAsync(Chunked("  \t# indented\na,b\n"), options);

        Assert.Contains(rows, r => r.Length == 2 && r[0] == "a");
    }

    [Fact]
    public async Task CommentLine_EndingInCrLf_IsConsumedWhole()
    {
        var options = new CsvReadOptions { CommentCharacter = '#' };
        var rows = await ReadRowsAsync(Chunked("#note\r\na,b\r\n"), options);

        Assert.Contains(rows, r => r.Length == 2 && r[0] == "a" && r[1] == "b");
    }

    [Fact]
    public async Task EscapeCharacter_ProtectsTheFollowingCharacter()
    {
        var options = new CsvReadOptions { EscapeCharacter = '\\' };
        var rows = await ReadRowsAsync(Chunked("a,b\n\"x\\\"y\",2\n"), options);

        Assert.Equal(2, rows.Count);
        Assert.Equal("2", rows[1][1]);
    }

    [Fact]
    public async Task Utf8Bom_IsStrippedEvenWhenSplitAcrossReads()
    {
        byte[] payload = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a,b\n1,2\n")];
        var rows = await ReadRowsAsync(Chunked(payload, chunkSize: 1));

        Assert.Equal(2, rows.Count);
        Assert.Equal("a", rows[0][0]);
    }

    [Fact]
    public async Task InputShorterThanABom_IsNotMistakenForOne()
    {
        var rows = await ReadRowsAsync(Chunked("a\n", chunkSize: 1));
        Assert.Equal(["a"], Assert.Single(rows));
    }

    [Fact]
    public async Task FinalRowWithoutATrailingNewline_IsYielded()
    {
        var rows = await ReadRowsAsync(Chunked("a,b\n1,2"));

        Assert.Equal(2, rows.Count);
        Assert.Equal(["1", "2"], rows[1]);
    }

    [Fact]
    public async Task EmptyInput_YieldsNothing()
        => Assert.Empty(await ReadRowsAsync(Chunked(string.Empty)));

    [Fact]
    public async Task NullReader_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in Csv.ReadFromPipeReaderAsync(null!, cancellationToken: TestContext.Current.CancellationToken))
            {
                // Enumeration never starts; the guard runs first.
            }
        });

    // ---- sequence reader -------------------------------------------------------

    [Fact]
    public async Task SequenceReader_ReadsRowsSplitAcrossSegments()
    {
        await using var reader = Csv.CreatePipeSequenceReader(Chunked("a,b,c\n1,2,3\n"));

        var rows = new List<string[]>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            var row = reader.Current;
            var columns = new string[row.ColumnCount];
            for (int i = 0; i < row.ColumnCount; i++)
            {
                columns[i] = Encoding.UTF8.GetString(row[i].ToArray());
            }
            rows.Add(columns);
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public async Task SequenceReader_ColumnSpanningSegments_CopiesOut()
    {
        // A column that straddles two segments is not a single span, so callers have to be
        // able to materialise it — that is what ToArray and TryCopyTo are for.
        await using var reader = Csv.CreatePipeSequenceReader(Chunked("value\nabcdefghijklmnop\n", chunkSize: 2));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));

        var column = reader.Current[0];
        Assert.Equal(16, column.Length);

        byte[] destination = new byte[column.Length];
        Assert.True(column.TryCopyTo(destination));
        Assert.Equal("abcdefghijklmnop", Encoding.UTF8.GetString(destination));
        Assert.Equal("abcdefghijklmnop", Encoding.UTF8.GetString(column.ToArray()));
    }

    [Fact]
    public async Task SequenceReader_TooSmallDestination_IsRefused()
    {
        await using var reader = Csv.CreatePipeSequenceReader(Chunked("value\nabcdef\n", chunkSize: 2));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));

        Assert.False(reader.Current[0].TryCopyTo(new byte[2]));
    }

    [Fact]
    public async Task SequenceReader_TracksRowNumbers()
    {
        await using var reader = Csv.CreatePipeSequenceReader(Chunked("a\nb\nc\n"));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, reader.Current.RowNumber);
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, reader.Current.RowNumber);
    }

    [Fact]
    public async Task SequenceReader_ExposesTheRawRecord()
    {
        await using var reader = Csv.CreatePipeSequenceReader(Chunked("a,b\n"));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        var raw = reader.Current.RawRecord;
        Assert.Equal("a,b", Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(in raw)));
    }

    [Fact]
    public async Task SequenceReader_StripsAUtf8Bom()
    {
        byte[] payload = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a,b\n")];
        await using var reader = Csv.CreatePipeSequenceReader(Chunked(payload, chunkSize: 1));

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("a", Encoding.UTF8.GetString(reader.Current[0].ToArray()));
    }

    [Fact]
    public async Task SequenceReader_EmptyInput_HasNoRows()
    {
        await using var reader = Csv.CreatePipeSequenceReader(Chunked(string.Empty));
        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SequenceReader_NullReader_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.CreatePipeSequenceReader(null!));

    // ---- sequence reader options and limits ------------------------------------

    private static async Task<List<string[]>> ReadSequenceRowsAsync(PipeReader reader, CsvReadOptions options)
    {
        await using var sequenceReader = Csv.CreatePipeSequenceReader(reader, options);

        var rows = new List<string[]>();
        while (await sequenceReader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            var row = sequenceReader.Current;
            var columns = new string[row.ColumnCount];
            for (int i = 0; i < row.ColumnCount; i++)
            {
                columns[i] = Encoding.UTF8.GetString(row[i].ToArray());
            }
            rows.Add(columns);
        }
        return rows;
    }

    [Fact]
    public async Task SequenceReader_EscapeCharacter_ProtectsTheFollowingByte()
    {
        // The escaped delimiter must not split the field, even when the escape and the
        // character it protects land in different reads.
        var options = new CsvReadOptions { EscapeCharacter = '\\' };
        var rows = await ReadSequenceRowsAsync(Chunked("a,b\n\"x\\,y\",2\n"), options);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[1].Length);
    }

    [Fact]
    public async Task SequenceReader_CommentCharacter_SkipsCommentLines()
    {
        var options = new CsvReadOptions { CommentCharacter = '#', TrackSourceLineNumbers = true };
        var rows = await ReadSequenceRowsAsync(Chunked("#note\na,b\n#again\n1,2\n"), options);

        Assert.Contains(rows, r => r.Length == 2 && r[0] == "a");
        Assert.Contains(rows, r => r.Length == 2 && r[0] == "1");
    }

    [Fact]
    public async Task SequenceReader_TracksSourceLineNumbersAcrossComments()
    {
        var options = new CsvReadOptions { CommentCharacter = '#', TrackSourceLineNumbers = true };
        await using var reader = Csv.CreatePipeSequenceReader(Chunked("#c\na,b\n"), options);

        // The first yielded row is the second physical line of the file.
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.True(reader.Current.SourceLineNumber >= 1);
    }

    [Fact]
    public async Task SequenceReader_NewlineInsideQuotes_IsRejectedUnlessAllowed()
    {
        var options = new CsvReadOptions { AllowNewlinesInsideQuotes = false };
        var ex = await Assert.ThrowsAsync<CsvException>(
            () => ReadSequenceRowsAsync(Chunked("a\n\"line1\nline2\"\n"), options));

        Assert.Equal(CsvErrorCode.ParseError, ex.ErrorCode);
    }

    [Fact]
    public async Task SequenceReader_UnterminatedQuote_IsReported()
    {
        var options = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        await Assert.ThrowsAsync<CsvException>(
            () => ReadSequenceRowsAsync(Chunked("a\n\"never closed\n"), options));
    }

    [Fact]
    public async Task SequenceReader_TooManyColumns_IsReported()
    {
        var options = new CsvReadOptions { MaxColumnCount = 2 };
        var ex = await Assert.ThrowsAsync<CsvException>(
            () => ReadSequenceRowsAsync(Chunked("1,2,3,4\n"), options));

        Assert.Equal(CsvErrorCode.TooManyColumns, ex.ErrorCode);
    }

    [Fact]
    public async Task SequenceReader_OversizedField_IsReported()
    {
        var options = new CsvReadOptions { MaxFieldSize = 4 };
        var ex = await Assert.ThrowsAsync<CsvException>(
            () => ReadSequenceRowsAsync(Chunked("abcdefghij,2\n"), options));

        Assert.Equal(CsvErrorCode.ParseError, ex.ErrorCode);
    }

    [Fact]
    public async Task SequenceReader_OversizedRow_IsReported()
    {
        var options = new CsvReadOptions { MaxRowSize = 8 };
        await Assert.ThrowsAsync<CsvException>(
            () => ReadSequenceRowsAsync(Chunked("aaaaaaaaaaaaaaaaaaaa,b\n"), options));
    }
}
