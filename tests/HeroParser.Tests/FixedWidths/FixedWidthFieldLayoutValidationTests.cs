using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records.Binding;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

public class FixedWidthFieldLayoutValidationTests
{
    private sealed class OverlappingRecord
    {
        [FixedWidthColumn(Start = 0, Length = 5)]
        public string? First { get; set; }

        [FixedWidthColumn(Start = 3, Length = 5)]
        public string? Second { get; set; }
    }

    private sealed class ZeroLengthRecord
    {
        [FixedWidthColumn(Start = 0, Length = 0)]
        public string? Value { get; set; }
    }

    [Fact]
    public void Write_OverlappingFields_Throws()
    {
        var records = new[] { new OverlappingRecord { First = "A", Second = "B" } };

        var ex = Assert.Throws<FixedWidthException>(() =>
            FixedWidth.Write<OverlappingRecord>().ToText(records));

        Assert.Equal(FixedWidthErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    public void Read_ZeroLengthField_Throws()
    {
        var ex = Assert.Throws<FixedWidthException>(() =>
            FixedWidth.DeserializeRecords<ZeroLengthRecord>("ABCDE"));

        Assert.Equal(FixedWidthErrorCode.InvalidOptions, ex.ErrorCode);
    }
}
