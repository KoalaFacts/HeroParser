using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthRecordLimitTrailingIgnoredRowsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void LineBasedMaxRecordCount_ExcludesTrailingCommentsAndSkippedEmptyLines(
        bool useUtf8,
        bool trackSourceLineNumbers,
        bool hasExtraRecord)
    {
        string input = hasExtraRecord ? "A\n# ignored\n\nB\n" : "A\n# ignored\n\n";
        var options = new FixedWidthReadOptions
        {
            MaxRecordCount = 1,
            CommentCharacter = '#',
            SkipEmptyLines = true,
            TrackSourceLineNumbers = trackSourceLineNumbers
        };

        var rows = new List<(string Text, int RecordNumber, int SourceLineNumber)>();

        if (hasExtraRecord)
        {
            var error = Assert.Throws<FixedWidthException>(() => ReadRows(input, options, useUtf8, rows));
            Assert.Equal(FixedWidthErrorCode.TooManyRecords, error.ErrorCode);
        }
        else
        {
            ReadRows(input, options, useUtf8, rows);
        }

        Assert.Single(rows);
        Assert.Equal(("A", 1, trackSourceLineNumbers ? 1 : 0), rows[0]);
    }

    private static void ReadRows(
        string input,
        FixedWidthReadOptions options,
        bool useUtf8,
        List<(string Text, int RecordNumber, int SourceLineNumber)> rows)
    {
        if (useUtf8)
        {
            foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(Encoding.UTF8.GetBytes(input), options))
            {
                rows.Add((row.ToDecodedString(), row.RecordNumber, row.SourceLineNumber));
            }
        }
        else
        {
            foreach (var row in FixedWidth.ReadFromText(input, options))
            {
                rows.Add((row.RawRecord.ToString(), row.RecordNumber, row.SourceLineNumber));
            }
        }
    }
}
