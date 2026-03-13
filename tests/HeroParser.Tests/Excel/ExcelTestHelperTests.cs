using HeroParser.Excels.Xlsx;
using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelTestHelperTests
{
    [Fact]
    public void CreateXlsx_SingleSheet_ProducesValidXlsx()
    {
        string[][] rows =
        [
            ["Name", "Age", "City"],
            ["Alice", "30", "Seattle"],
            ["Bob", "25", "Portland"]
        ];

        using var stream = ExcelTestHelper.CreateXlsx("Sheet1", rows);
        using var reader = new XlsxReader(stream);

        var sheet = reader.Workbook.GetFirstSheet();
        Assert.Equal("Sheet1", sheet.Name);

        using var sheetReader = reader.OpenSheet(sheet);

        var row1 = sheetReader.ReadNextRow();
        Assert.NotNull(row1);
        Assert.Equal(["Name", "Age", "City"], row1);

        var row2 = sheetReader.ReadNextRow();
        Assert.NotNull(row2);
        Assert.Equal("Alice", row2[0]);
        Assert.Equal("30", row2[1]);
        Assert.Equal("Seattle", row2[2]);

        var row3 = sheetReader.ReadNextRow();
        Assert.NotNull(row3);
        Assert.Equal("Bob", row3[0]);
        Assert.Equal("25", row3[1]);
        Assert.Equal("Portland", row3[2]);

        var row4 = sheetReader.ReadNextRow();
        Assert.Null(row4);
    }

    [Fact]
    public void CreateXlsx_MultiSheet_AllSheetsAccessible()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["People"] = [["Name"], ["Alice"]],
            ["Numbers"] = [["Value"], ["42"]]
        };

        using var stream = ExcelTestHelper.CreateXlsx(sheets);
        using var reader = new XlsxReader(stream);

        Assert.Equal(2, reader.Workbook.Sheets.Count);
        Assert.Equal("People", reader.Workbook.Sheets[0].Name);
        Assert.Equal("Numbers", reader.Workbook.Sheets[1].Name);

        // Verify first sheet
        using var sheet1 = reader.OpenSheet(reader.Workbook.GetSheetByName("People"));
        var header1 = sheet1.ReadNextRow();
        Assert.NotNull(header1);
        Assert.Equal("Name", header1[0]);

        var data1 = sheet1.ReadNextRow();
        Assert.NotNull(data1);
        Assert.Equal("Alice", data1[0]);
    }

    [Fact]
    public void CreateXlsxWithDates_ProducesDateFormattedCells()
    {
        string[] headers = ["Date"];
        // OLE date 44927 = 2023-01-01
        (double, int)[][] dataRows = [[(44927.0, 1)]]; // styleId=1 maps to date format

        using var stream = ExcelTestHelper.CreateXlsxWithDates("Dates", headers, dataRows);
        using var reader = new XlsxReader(stream);

        using var sheetReader = reader.OpenSheet(reader.Workbook.GetFirstSheet());

        var header = sheetReader.ReadNextRow();
        Assert.NotNull(header);
        Assert.Equal("Date", header[0]);

        var row = sheetReader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("2023-01-01T00:00:00", row[0]);
    }

    [Fact]
    public void CreateXlsx_SpecialCharacters_EscapedCorrectly()
    {
        string[][] rows =
        [
            ["Header"],
            ["<Hello & World>"]
        ];

        using var stream = ExcelTestHelper.CreateXlsx("Test", rows);
        using var reader = new XlsxReader(stream);

        using var sheetReader = reader.OpenSheet(reader.Workbook.GetFirstSheet());

        var header = sheetReader.ReadNextRow();
        Assert.NotNull(header);
        Assert.Equal("Header", header[0]);

        var data = sheetReader.ReadNextRow();
        Assert.NotNull(data);
        Assert.Equal("<Hello & World>", data[0]);
    }
}
