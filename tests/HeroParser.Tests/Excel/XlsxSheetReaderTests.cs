using HeroParser.Excel.Xlsx;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class XlsxSheetReaderTests
{
    [Fact]
    public void ReadNextRow_Simple3x3Sheet_ReturnsCorrectRows()
    {
        var sharedStrings = CreateSharedStrings(["Name", "Age", "City", "Alice", "Bob"]);
        var stylesheet = XlsxStylesheet.Parse(null);

        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="s"><v>0</v></c>
                  <c r="B1" t="s"><v>1</v></c>
                  <c r="C1" t="s"><v>2</v></c>
                </row>
                <row r="2">
                  <c r="A2" t="s"><v>3</v></c>
                  <c r="B2"><v>30</v></c>
                  <c r="C2" t="s"><v>2</v></c>
                </row>
                <row r="3">
                  <c r="A3" t="s"><v>4</v></c>
                  <c r="B3"><v>25</v></c>
                  <c r="C3" t="s"><v>2</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);

        var row1 = reader.ReadNextRow();
        Assert.NotNull(row1);
        Assert.Equal(["Name", "Age", "City"], row1);
        Assert.Equal(1, reader.CurrentRowNumber);

        var row2 = reader.ReadNextRow();
        Assert.NotNull(row2);
        Assert.Equal(["Alice", "30", "City"], row2);
        Assert.Equal(2, reader.CurrentRowNumber);

        var row3 = reader.ReadNextRow();
        Assert.NotNull(row3);
        Assert.Equal(["Bob", "25", "City"], row3);
        Assert.Equal(3, reader.CurrentRowNumber);

        var row4 = reader.ReadNextRow();
        Assert.Null(row4);
    }

    [Fact]
    public void ReadNextRow_SharedStringResolution_ReturnsCorrectValues()
    {
        var sharedStrings = CreateSharedStrings(["Hello", "World"]);
        var stylesheet = XlsxStylesheet.Parse(null);

        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="s"><v>0</v></c>
                  <c r="B1" t="s"><v>1</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("Hello", row[0]);
        Assert.Equal("World", row[1]);
    }

    [Fact]
    public void ReadNextRow_InlineStrings_ReturnsCorrectValues()
    {
        var sharedStrings = XlsxSharedStrings.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """));
        var stylesheet = XlsxStylesheet.Parse(null);

        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="inlineStr"><is><t>Inline Text</t></is></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("Inline Text", row[0]);
    }

    [Fact]
    public void ReadNextRow_BooleanCells_ReturnsFormattedValues()
    {
        var sharedStrings = XlsxSharedStrings.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """));
        var stylesheet = XlsxStylesheet.Parse(null);

        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="b"><v>1</v></c>
                  <c r="B1" t="b"><v>0</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("TRUE", row[0]);
        Assert.Equal("FALSE", row[1]);
    }

    [Fact]
    public void ReadNextRow_ErrorCells_ReturnsEmptyString()
    {
        var sharedStrings = XlsxSharedStrings.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """));
        var stylesheet = XlsxStylesheet.Parse(null);

        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="e"><v>#DIV/0!</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("", row[0]);
    }

    [Fact]
    public void ReadNextRow_NumericCells_ReturnsRawNumber()
    {
        var sharedStrings = XlsxSharedStrings.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """));
        var stylesheet = XlsxStylesheet.Parse(null);

        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1"><v>42.5</v></c>
                  <c r="B1"><v>100</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("42.5", row[0]);
        Assert.Equal("100", row[1]);
    }

    [Fact]
    public void ReadNextRow_DateCells_ReturnsFormattedDate()
    {
        var sharedStrings = XlsxSharedStrings.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """));

        // Style index 0 maps to built-in format ID 14 (date)
        var stylesheet = XlsxStylesheet.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs count="1">
                <xf numFmtId="14" />
              </cellXfs>
            </styleSheet>
            """));

        // OLE date 44927 = 2023-01-01
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" s="0"><v>44927</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        // DateTime.FromOADate(44927) = 2023-01-01T00:00:00
        Assert.Equal("2023-01-01T00:00:00", row[0]);
    }

    [Fact]
    public void ReadNextRow_SparseRow_FillsGapsWithEmpty()
    {
        var sharedStrings = CreateSharedStrings(["A", "C"]);
        var stylesheet = XlsxStylesheet.Parse(null);

        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="s"><v>0</v></c>
                  <c r="C1" t="s"><v>1</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal(3, row.Length);
        Assert.Equal("A", row[0]);
        Assert.Equal("", row[1]); // B1 is missing, should be empty
        Assert.Equal("C", row[2]);
    }

    [Fact]
    public void ReadNextRow_EmptySheet_ReturnsNull()
    {
        var sharedStrings = XlsxSharedStrings.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """));
        var stylesheet = XlsxStylesheet.Parse(null);

        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData />
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.Null(row);
    }

    [Fact]
    public void ReadNextRow_FormulaStringResult_ReturnsValue()
    {
        var sharedStrings = XlsxSharedStrings.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """));
        var stylesheet = XlsxStylesheet.Parse(null);

        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="str"><v>Calculated</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("Calculated", row[0]);
    }

    [Fact]
    public void ReadNextRow_TimeOnlyValue_ReturnsTimeSpanString()
    {
        var sharedStrings = XlsxSharedStrings.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """));

        var stylesheet = XlsxStylesheet.Parse(CreateStream(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <cellXfs count="1">
                <xf numFmtId="20" />
              </cellXfs>
            </styleSheet>
            """));

        // 0.5 = 12:00:00 (noon)
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" s="0"><v>0.5</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("12:00:00", row[0]);
    }

    [Fact]
    public void ReadNextRow_WideColumns_HandlesAAToBeyondZ()
    {
        var sharedStrings = CreateSharedStrings(["First", "Last"]);
        var stylesheet = XlsxStylesheet.Parse(null);

        // Column AA = index 26
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="s"><v>0</v></c>
                  <c r="AA1" t="s"><v>1</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;

        using var reader = new XlsxSheetReader(CreateStream(xml), sharedStrings, stylesheet);
        var row = reader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal(27, row.Length); // A=0 through AA=26
        Assert.Equal("First", row[0]);
        Assert.Equal("Last", row[26]);
        // All gaps should be empty
        for (int i = 1; i < 26; i++)
            Assert.Equal("", row[i]);
    }

    private static XlsxSharedStrings CreateSharedStrings(string[] values)
    {
        var siElements = string.Join("\n", values.Select((v, _) => $"<si><t>{System.Security.SecurityElement.Escape(v)}</t></si>"));
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="{values.Length}" uniqueCount="{values.Length}">
              {siElements}
            </sst>
            """;
        return XlsxSharedStrings.Parse(CreateStream(xml));
    }

    private static Stream CreateStream(string xml)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
}
