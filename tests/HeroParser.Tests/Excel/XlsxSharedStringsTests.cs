using HeroParser.Excel.Xlsx;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class XlsxSharedStringsTests
{
    [Fact]
    public void Parse_SimpleStrings_ReturnsCorrectValues()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="3" uniqueCount="3">
              <si><t>Hello</t></si>
              <si><t>World</t></si>
              <si><t>Test</t></si>
            </sst>
            """;
        var strings = XlsxSharedStrings.Parse(CreateStream(xml));
        Assert.Equal(3, strings.Count);
        Assert.Equal("Hello", strings[0]);
        Assert.Equal("World", strings[1]);
        Assert.Equal("Test", strings[2]);
    }

    [Fact]
    public void Parse_RichTextStrings_ConcatenatesRuns()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="1" uniqueCount="1">
              <si><r><t>Hello </t></r><r><t>World</t></r></si>
            </sst>
            """;
        var strings = XlsxSharedStrings.Parse(CreateStream(xml));
        Assert.Equal(1, strings.Count);
        Assert.Equal("Hello World", strings[0]);
    }

    [Fact]
    public void Parse_EmptyTable_ReturnsEmptyList()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """;
        var strings = XlsxSharedStrings.Parse(CreateStream(xml));
        Assert.Equal(0, strings.Count);
    }

    [Fact]
    public void Parse_PreservedWhitespace_RetainsSpaces()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="1" uniqueCount="1">
              <si><t xml:space="preserve">  Hello  </t></si>
            </sst>
            """;
        var strings = XlsxSharedStrings.Parse(CreateStream(xml));
        Assert.Equal("  Hello  ", strings[0]);
    }

    private static Stream CreateStream(string xml)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
}
