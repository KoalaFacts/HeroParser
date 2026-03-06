using HeroParser.Conversion;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for CSV-to-FixedWidth and FixedWidth-to-CSV conversion.
/// </summary>
public class ConversionTests
{
    #region CSV to FixedWidth

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_SimpleData_ConvertsCorrectly()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
            new FixedWidthFieldDefinition("City", 10),
        };

        var result = CsvToFixedWidthConverter.Convert(csv, columns);

        var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Alice     30   NYC       ", lines[0]);
        Assert.Equal("Bob       25   LA        ", lines[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_RightAligned_AlignsCorrectly()
    {
        var csv = "Name,Amount\r\nAlice,1500\r\nBob,250\r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10, FieldAlignment.Left),
            new FixedWidthFieldDefinition("Amount", 10, FieldAlignment.Right),
        };

        var result = CsvToFixedWidthConverter.Convert(csv, columns);

        var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Alice          1500", lines[0]);
        Assert.Equal("Bob             250", lines[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_CustomPadChar_UsesPadChar()
    {
        var csv = "Id,Name\r\n1,Alice\r\n2,Bob\r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Id", 5, FieldAlignment.Right, '0'),
            new FixedWidthFieldDefinition("Name", 10),
        };

        var result = CsvToFixedWidthConverter.Convert(csv, columns);

        var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("00001Alice     ", lines[0]);
        Assert.Equal("00002Bob       ", lines[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_Truncation_TruncatesLongValues()
    {
        var csv = "Name\r\nAlexander Hamilton\r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
        };

        var result = CsvToFixedWidthConverter.Convert(csv, columns);

        var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Alexander ", lines[0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_CustomDelimiter_ParsesCorrectly()
    {
        var csv = "Name;Age\r\nAlice;30\r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
        };
        var options = new CsvToFixedWidthOptions { Delimiter = ';' };

        var result = CsvToFixedWidthConverter.Convert(csv, columns, options);

        var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Alice     30   ", lines[0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_IncludeHeader_WritesHeader()
    {
        var csv = "Name,Age\r\nAlice,30\r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
        };
        var options = new CsvToFixedWidthOptions { IncludeHeader = true };

        var result = CsvToFixedWidthConverter.Convert(csv, columns, options);

        var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Name      Age  ", lines[0]);
        Assert.Equal("Alice     30   ", lines[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_EmptyInput_ReturnsEmpty()
    {
        var csv = "Name,Age\r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
        };

        var result = CsvToFixedWidthConverter.Convert(csv, columns);

        Assert.Equal("", result);
    }

    #endregion

    #region FixedWidth to CSV

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthToCsv_SimpleData_ConvertsCorrectly()
    {
        var data = "Alice     30   NYC       \r\nBob       25   LA        \r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
            new FixedWidthFieldDefinition("City", 10),
        };

        var result = FixedWidthToCsvConverter.Convert(data, columns);

        Assert.Equal("Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthToCsv_RightAligned_TrimsCorrectly()
    {
        var data = "00001Alice     \r\n00002Bob       \r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Id", 5, FieldAlignment.Right, '0'),
            new FixedWidthFieldDefinition("Name", 10),
        };

        var result = FixedWidthToCsvConverter.Convert(data, columns);

        Assert.Equal("Id,Name\r\n1,Alice\r\nBob\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthToCsv_CustomDelimiter_UsesDelimiter()
    {
        var data = "Alice     30   \r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
        };
        var options = new FixedWidthToCsvOptions { Delimiter = ';' };

        var result = FixedWidthToCsvConverter.Convert(data, columns, options);

        Assert.Equal("Name;Age\r\nAlice;30\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthToCsv_WithoutHeader_OmitsHeader()
    {
        var data = "Alice     30   \r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
        };
        var options = new FixedWidthToCsvOptions { IncludeHeader = false };

        var result = FixedWidthToCsvConverter.Convert(data, columns, options);

        Assert.Equal("Alice,30\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthToCsv_ValuesNeedingQuoting_QuotesCorrectly()
    {
        var data = "Has, comma30   \r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
        };

        var result = FixedWidthToCsvConverter.Convert(data, columns);

        // The value "Has, comma" should be quoted because it contains the delimiter
        Assert.Contains("\"Has, comm\"", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthToCsv_EmptyInput_ReturnsHeaderOnly()
    {
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
        };

        var result = FixedWidthToCsvConverter.Convert("", columns);

        Assert.Equal("Name,Age\r\n", result);
    }

    #endregion

    #region Round-Trip

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_CsvToFixedWidthAndBack_PreservesData()
    {
        var originalCsv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";
        var columns = new[]
        {
            new FixedWidthFieldDefinition("Name", 10),
            new FixedWidthFieldDefinition("Age", 5),
            new FixedWidthFieldDefinition("City", 10),
        };

        var fixedWidth = CsvToFixedWidthConverter.Convert(originalCsv, columns);
        var roundTripCsv = FixedWidthToCsvConverter.Convert(fixedWidth, columns);

        Assert.Equal(originalCsv, roundTripCsv);
    }

    #endregion

    #region Argument Validation

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_NullData_ThrowsArgumentNullException()
    {
        var columns = new[] { new FixedWidthFieldDefinition("Name", 10) };
        Assert.Throws<ArgumentNullException>(() => CsvToFixedWidthConverter.Convert(null!, columns));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_NullColumns_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CsvToFixedWidthConverter.Convert("a\n1", null!));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToFixedWidth_EmptyColumns_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CsvToFixedWidthConverter.Convert("a\n1", []));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthFieldDefinition_ZeroWidth_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedWidthFieldDefinition("Name", 0));
    }

    #endregion
}
