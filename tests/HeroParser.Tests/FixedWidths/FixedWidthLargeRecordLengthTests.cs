using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthLargeRecordLengthTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf8Span_LargeRecordLengthAfterBom_ReportsPartialRecord(bool trackSourceLineNumbers)
    {
        byte[] input = [0xEF, 0xBB, 0xBF, (byte)'X'];
        var options = new FixedWidthReadOptions
        {
            RecordLength = int.MaxValue,
            TrackSourceLineNumbers = trackSourceLineNumbers
        };

        var error = Assert.Throws<FixedWidthException>(() =>
        {
            CountRows(input, options);
        });

        Assert.Equal(FixedWidthErrorCode.InvalidRecordLength, error.ErrorCode);
        Assert.Equal(1, error.Record);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Utf8Span_SkippingOversizedFirstRecord_DoesNotOverflow(
        bool trackSourceLineNumbers,
        bool hasHeaderRow)
    {
        byte[] input = [0xEF, 0xBB, 0xBF, (byte)'X'];
        var options = new FixedWidthReadOptions
        {
            RecordLength = int.MaxValue,
            SkipRows = hasHeaderRow ? 0 : 1,
            HasHeaderRow = hasHeaderRow,
            TrackSourceLineNumbers = trackSourceLineNumbers
        };

        Assert.Equal(0, CountRows(input, options));
    }

    private static int CountRows(byte[] input, FixedWidthReadOptions options)
    {
        int count = 0;
        foreach (var _ in FixedWidth.ReadFromUtf8ByteSpan(input, options))
        {
            count++;
        }

        return count;
    }
}
