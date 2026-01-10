using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests;

public class CsvLineNumberTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TrackSourceLineNumbers_WithCarriageReturnLineEndings()
    {
        var options = new CsvParserOptions { TrackSourceLineNumbers = true };
        var csv = "a,b\r1,2\r3,4\r";
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        Assert.Equal(1, reader.Current.SourceLineNumber);

        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current.SourceLineNumber);

        Assert.True(reader.MoveNext());
        Assert.Equal(3, reader.Current.SourceLineNumber);

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TrackSourceLineNumbers_WithCarriageReturnsInsideQuotes()
    {
        var options = new CsvParserOptions
        {
            TrackSourceLineNumbers = true,
            AllowNewlinesInsideQuotes = true
        };
        var csv = "a,\"b\rc\",d\r1,2,3\r";
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        Assert.Equal(1, reader.Current.SourceLineNumber);

        Assert.True(reader.MoveNext());
        Assert.Equal(3, reader.Current.SourceLineNumber);
    }
}
