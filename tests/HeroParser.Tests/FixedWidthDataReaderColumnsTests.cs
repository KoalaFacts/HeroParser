using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Reading.Data;
using HeroParser.FixedWidths.Records.Binding;
using Xunit;

namespace HeroParser.Tests;

public class FixedWidthDataReaderColumnsTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Columns_FromLengths_BuildsSequentialColumns()
    {
        var columns = FixedWidthDataReaderColumns.FromLengths(
            [2, 3, 4],
            ["A", "B", "C"]);

        Assert.Equal(3, columns.Length);
        Assert.Equal(0, columns[0].Start);
        Assert.Equal(2, columns[0].Length);
        Assert.Equal("A", columns[0].Name);

        Assert.Equal(2, columns[1].Start);
        Assert.Equal(3, columns[1].Length);
        Assert.Equal("B", columns[1].Name);

        Assert.Equal(5, columns[2].Start);
        Assert.Equal(4, columns[2].Length);
        Assert.Equal("C", columns[2].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Columns_FromLengths_ThrowsOnNameMismatch()
    {
        Assert.Throws<ArgumentException>(() =>
            FixedWidthDataReaderColumns.FromLengths([2, 3], ["A"]));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Columns_FromAttributes_MapsSettings()
    {
        var attributes = new[]
        {
            new FixedWidthColumnAttribute { Start = 0, Length = 2, PadChar = '0', Alignment = FieldAlignment.Right },
            new FixedWidthColumnAttribute { Start = 2, Length = 3 }
        };

        var columns = FixedWidthDataReaderColumns.FromAttributes(attributes, ["Left", "Right"]);

        Assert.Equal(2, columns.Length);
        Assert.Equal(0, columns[0].Start);
        Assert.Equal(2, columns[0].Length);
        Assert.Equal('0', columns[0].PadChar);
        Assert.Equal(FieldAlignment.Right, columns[0].Alignment);
        Assert.Equal("Left", columns[0].Name);

        Assert.Equal(2, columns[1].Start);
        Assert.Equal(3, columns[1].Length);
        Assert.Null(columns[1].PadChar);
        Assert.Equal(FieldAlignment.Left, columns[1].Alignment);
        Assert.Equal("Right", columns[1].Name);
    }
}
