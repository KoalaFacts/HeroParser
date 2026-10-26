using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthAsyncStreamSourceLineNumberTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SourceLineNumbers_MatchSpanAndPipeSemanticsWhenTrackingIsOptional(
        bool fixedLength,
        bool trackSourceLineNumbers)
    {
        string input = fixedLength ? "SKHDABCD" : "SKIP\nHEAD\n#COMMENT\nA\n\nB\n";
        var options = new FixedWidthReadOptions
        {
            RecordLength = fixedLength ? 2 : null,
            SkipRows = 1,
            HasHeaderRow = true,
            CommentCharacter = '#',
            TrackSourceLineNumbers = trackSourceLineNumbers
        };

        (string Text, int RecordNumber, int SourceLineNumber)[] expected = fixedLength
            ? [("AB", 1, trackSourceLineNumbers ? 1 : 0), ("CD", 2, trackSourceLineNumbers ? 1 : 0)]
            : [("A", 1, trackSourceLineNumbers ? 4 : 0), ("B", 2, trackSourceLineNumbers ? 6 : 0)];

        // The opt-in contract is shared by borrowed UTF-16/UTF-8 rows and
        // the asynchronous stream reader, even after skipped headers/comments.
        Assert.Equal(expected, ReadTextRows(input, options));
        Assert.Equal(expected, ReadUtf8Rows(input, options));

        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(input), TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();
        var pipeRows = new List<(string Text, int RecordNumber, int SourceLineNumber)>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(
            pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            pipeRows.Add((row.ToDecodedString(), row.RecordNumber, row.SourceLineNumber));
        }

        Assert.Equal(expected, pipeRows);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input), writable: false);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options, leaveOpen: true);
        var streamRows = new List<(string Text, int RecordNumber, int SourceLineNumber)>();

        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            var row = reader.Current;
            streamRows.Add((row.RawRecord.ToString(), row.RecordNumber, row.SourceLineNumber));
        }

        Assert.Equal(expected, streamRows);
    }

    private static List<(string Text, int RecordNumber, int SourceLineNumber)> ReadTextRows(
        string input,
        FixedWidthReadOptions options)
    {
        var rows = new List<(string Text, int RecordNumber, int SourceLineNumber)>();
        foreach (var row in FixedWidth.ReadFromText(input, options))
        {
            rows.Add((row.RawRecord.ToString(), row.RecordNumber, row.SourceLineNumber));
        }
        return rows;
    }

    private static List<(string Text, int RecordNumber, int SourceLineNumber)> ReadUtf8Rows(
        string input,
        FixedWidthReadOptions options)
    {
        var rows = new List<(string Text, int RecordNumber, int SourceLineNumber)>();
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(Encoding.UTF8.GetBytes(input), options))
        {
            rows.Add((row.ToDecodedString(), row.RecordNumber, row.SourceLineNumber));
        }
        return rows;
    }
}
