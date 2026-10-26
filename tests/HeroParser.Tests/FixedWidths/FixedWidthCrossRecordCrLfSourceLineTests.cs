using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthCrossRecordCrLfSourceLineTests
{
    private const string INPUT = "A\r\nBXY";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Utf16Span_CrLfAcrossFixedLengthRecords_CountsOnePhysicalNewline(int skipRows)
    {
        var rows = new List<string>();
        var sourceLines = new List<int>();
        foreach (var row in FixedWidth.ReadFromText(INPUT, Options(skipRows)))
        {
            rows.Add(row.RawRecord.ToString());
            sourceLines.Add(row.SourceLineNumber);
        }

        AssertRows(skipRows, rows, sourceLines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Utf8Span_CrLfAcrossFixedLengthRecords_CountsOnePhysicalNewline(int skipRows)
    {
        var rows = new List<string>();
        var sourceLines = new List<int>();
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan("A\r\nBXY"u8, Options(skipRows)))
        {
            rows.Add(row.ToDecodedString());
            sourceLines.Add(row.SourceLineNumber);
        }

        AssertRows(skipRows, rows, sourceLines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task AsyncStream_CrLfAcrossFixedLengthRecords_CountsOnePhysicalNewline(int skipRows)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(INPUT), writable: false);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, Options(skipRows), leaveOpen: true);
        var rows = new List<string>();
        var sourceLines = new List<int>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(reader.Current.RawRecord.ToString());
            sourceLines.Add(reader.Current.SourceLineNumber);
        }

        AssertRows(skipRows, rows, sourceLines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PipeReader_CrLfAcrossFixedLengthRecords_CountsOnePhysicalNewline(int skipRows)
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(INPUT), TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();

        var rows = new List<string>();
        var sourceLines = new List<int>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(
            pipe.Reader, Options(skipRows), TestContext.Current.CancellationToken))
        {
            rows.Add(row.ToDecodedString());
            sourceLines.Add(row.SourceLineNumber);
        }

        AssertRows(skipRows, rows, sourceLines);
    }

    private static FixedWidthReadOptions Options(int skipRows) => new()
    {
        RecordLength = 2,
        SkipRows = skipRows,
        TrackSourceLineNumbers = true
    };

    private static void AssertRows(int skipRows, List<string> rows, List<int> sourceLines)
    {
        if (skipRows == 0)
        {
            Assert.Equal(["A\r", "\nB", "XY"], rows);
            Assert.Equal([1, 2, 2], sourceLines);
        }
        else
        {
            Assert.Equal(["\nB", "XY"], rows);
            Assert.Equal([2, 2], sourceLines);
        }
    }
}
