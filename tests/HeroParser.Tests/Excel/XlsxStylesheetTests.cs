using HeroParser.Excel.Xlsx;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class XlsxStylesheetTests
{
    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    public void IsDateFormat_BuiltInDateFormatIds_ReturnsTrue(int numFmtId)
    {
        // Stylesheet with a single cellXf referencing the given built-in format ID
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs count="1">
                <xf numFmtId="{numFmtId}" />
              </cellXfs>
            </styleSheet>
            """;
        var stylesheet = XlsxStylesheet.Parse(CreateStream(xml));
        Assert.True(stylesheet.IsDateFormat(0));
    }

    [Fact]
    public void IsDateFormat_CustomDateFormat_ReturnsTrue()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="1">
                <numFmt numFmtId="164" formatCode="yyyy-mm-dd" />
              </numFmts>
              <cellXfs count="1">
                <xf numFmtId="164" />
              </cellXfs>
            </styleSheet>
            """;
        var stylesheet = XlsxStylesheet.Parse(CreateStream(xml));
        Assert.True(stylesheet.IsDateFormat(0));
    }

    [Theory]
    [InlineData("0.00")]
    [InlineData("#,##0")]
    [InlineData("0%")]
    [InlineData("#,##0.00")]
    public void IsDateFormat_NumberFormats_ReturnsFalse(string formatCode)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="1">
                <numFmt numFmtId="164" formatCode="{formatCode}" />
              </numFmts>
              <cellXfs count="1">
                <xf numFmtId="164" />
              </cellXfs>
            </styleSheet>
            """;
        var stylesheet = XlsxStylesheet.Parse(CreateStream(xml));
        Assert.False(stylesheet.IsDateFormat(0));
    }

    [Fact]
    public void Parse_NullStream_ReturnsEmptyStylesheet()
    {
        var stylesheet = XlsxStylesheet.Parse(null);
        Assert.False(stylesheet.IsDateFormat(0));
    }

    [Fact]
    public void IsDateFormat_MultipleStyleIndices_MapCorrectly()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="1">
                <numFmt numFmtId="164" formatCode="yyyy-mm-dd" />
              </numFmts>
              <cellXfs count="3">
                <xf numFmtId="0" />
                <xf numFmtId="164" />
                <xf numFmtId="1" />
              </cellXfs>
            </styleSheet>
            """;
        var stylesheet = XlsxStylesheet.Parse(CreateStream(xml));
        Assert.False(stylesheet.IsDateFormat(0)); // General format
        Assert.True(stylesheet.IsDateFormat(1));  // Custom date format
        Assert.False(stylesheet.IsDateFormat(2)); // Number format (0)
    }

    [Fact]
    public void IsDateFormat_OutOfRangeStyleIndex_ReturnsFalse()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs count="1">
                <xf numFmtId="0" />
              </cellXfs>
            </styleSheet>
            """;
        var stylesheet = XlsxStylesheet.Parse(CreateStream(xml));
        Assert.False(stylesheet.IsDateFormat(99));
    }

    [Fact]
    public void IsDateFormat_CustomTimeFormat_ReturnsTrue()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="1">
                <numFmt numFmtId="164" formatCode="h:mm:ss AM/PM" />
              </numFmts>
              <cellXfs count="1">
                <xf numFmtId="164" />
              </cellXfs>
            </styleSheet>
            """;
        var stylesheet = XlsxStylesheet.Parse(CreateStream(xml));
        Assert.True(stylesheet.IsDateFormat(0));
    }

    private static Stream CreateStream(string xml)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
}
