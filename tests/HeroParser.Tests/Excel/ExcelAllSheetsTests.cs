using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelAllSheetsTests
{
    [Fact]
    public void AllSheets_SameType_ReturnsDictionaryBySheetName()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["Q1"] = [["Name", "Value"], ["A", "1"]],
            ["Q2"] = [["Name", "Value"], ["B", "2"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var result = HeroParser.Excel.Read<NameValue>().AllSheets().FromStream(xlsx);

        Assert.Equal(2, result.Count);
        Assert.Single(result["Q1"]);
        Assert.Equal("A", result["Q1"][0].Name);
        Assert.Equal("1", result["Q1"][0].Value);
        Assert.Single(result["Q2"]);
        Assert.Equal("B", result["Q2"][0].Name);
        Assert.Equal("2", result["Q2"][0].Value);
    }

    [Fact]
    public void AllSheets_EmptyWorkbook_ReturnsEmptyDictionaries()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["Empty1"] = [["Name", "Value"]],
            ["Empty2"] = [["Name", "Value"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var result = HeroParser.Excel.Read<NameValue>().AllSheets().FromStream(xlsx);

        Assert.Equal(2, result.Count);
        Assert.Empty(result["Empty1"]);
        Assert.Empty(result["Empty2"]);
    }

    [Fact]
    public void AllSheets_SingleSheet_ReturnsSingleEntry()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Only", [
            ["Name", "Value"],
            ["X", "99"]
        ]);

        var result = HeroParser.Excel.Read<NameValue>().AllSheets().FromStream(xlsx);

        Assert.Single(result);
        Assert.True(result.ContainsKey("Only"));
        Assert.Single(result["Only"]);
        Assert.Equal("X", result["Only"][0].Name);
    }

    [Fact]
    public void AllSheets_MultipleRowsPerSheet()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["Data"] = [
                ["Name", "Value"],
                ["A", "1"],
                ["B", "2"],
                ["C", "3"]
            ],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var result = HeroParser.Excel.Read<NameValue>().AllSheets().FromStream(xlsx);

        Assert.Equal(3, result["Data"].Count);
        Assert.Equal("A", result["Data"][0].Name);
        Assert.Equal("C", result["Data"][2].Name);
    }
}
