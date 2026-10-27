using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthDecodedByteSpanBomTests
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
    public void Utf8Bom_DecodedByteSpanMatchesDirectUtf8WithHeadersAndSkippedRows(
        bool fixedLength,
        bool hasHeader,
        bool skipPreamble)
    {
        string terminator = fixedLength ? string.Empty : "\r\n";
        string preamble = skipPreamble ? "P" + terminator : string.Empty;
        string header = hasHeader ? "H" + terminator : string.Empty;
        string records = fixedLength ? "AB" : "A\r\n漢\r\n";
        byte[] input = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(preamble + header + records)];
        var options = new FixedWidthReadOptions
        {
            RecordLength = fixedLength ? 1 : null,
            SkipRows = skipPreamble ? 1 : 0,
            HasHeaderRow = hasHeader,
            TrackSourceLineNumbers = true
        };

        var expected = new List<(string Text, int Record, int SourceLine)>();
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(input, options))
        {
            expected.Add((row.ToDecodedString(), row.RecordNumber, row.SourceLineNumber));
        }

        var actual = new List<(string Text, int Record, int SourceLine)>();
        foreach (var row in FixedWidth.ReadFromByteSpan(input, options))
        {
            actual.Add((row.RawRecord.ToString(), row.RecordNumber, row.SourceLineNumber));
        }

        string[] expectedTexts = fixedLength ? ["A", "B"] : ["A", "漢"];
        Assert.Equal(expectedTexts, expected.Select(row => row.Text).ToArray());
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Utf8BomOnly_DoesNotProducePhantomRecord()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        int records = 0;
        foreach (var _ in FixedWidth.ReadFromByteSpan(bom))
        {
            records++;
        }

        Assert.Equal(0, records);
    }

    [Fact]
    public void IncompleteBomPrefix_RemainsDataWithUtf8Replacement()
    {
        byte[] input = [0xEF, 0xBB, (byte)'X', (byte)'\n'];
        string decoded = Encoding.UTF8.GetString(input.AsSpan(0, 3));
        int records = 0;
        foreach (var row in FixedWidth.ReadFromByteSpan(input))
        {
            Assert.Equal(decoded, row.RawRecord.ToString());
            records++;
        }

        Assert.Equal(1, records);
    }
}
