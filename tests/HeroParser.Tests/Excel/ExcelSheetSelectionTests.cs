using HeroParser.Excels.Core;
using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelSheetSelectionTests
{
    [Fact]
    public void DefaultSelection_ReadsFirstSheet()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["First"] = [["Name", "Value"], ["A", "1"]],
            ["Second"] = [["Name", "Value"], ["B", "2"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var records = HeroParser.Excel.Read<NameValue>().FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal("A", records[0].Name);
        Assert.Equal("1", records[0].Value);
    }

    [Fact]
    public void FromSheet_ByName_ReadsNamedSheet()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["First"] = [["Name", "Value"], ["A", "1"]],
            ["Second"] = [["Name", "Value"], ["B", "2"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var records = HeroParser.Excel.Read<NameValue>().FromSheet("Second").FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal("B", records[0].Name);
        Assert.Equal("2", records[0].Value);
    }

    [Fact]
    public void FromSheet_ByIndex_ReadsCorrectSheet()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["First"] = [["Name", "Value"], ["A", "1"]],
            ["Second"] = [["Name", "Value"], ["B", "2"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var records = HeroParser.Excel.Read<NameValue>().FromSheet(1).FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal("B", records[0].Name);
        Assert.Equal("2", records[0].Value);
    }

    [Fact]
    public void FromSheet_ByIndex_Zero_ReadsFirstSheet()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["First"] = [["Name", "Value"], ["A", "1"]],
            ["Second"] = [["Name", "Value"], ["B", "2"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var records = HeroParser.Excel.Read<NameValue>().FromSheet(0).FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal("A", records[0].Name);
    }

    [Fact]
    public void FromSheet_NonExistentName_ThrowsExcelException()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["Sheet1"] = [["Name", "Value"], ["A", "1"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var ex = Assert.Throws<ExcelException>(() =>
            HeroParser.Excel.Read<NameValue>().FromSheet("NonExistent").FromStream(xlsx));

        Assert.Contains("NonExistent", ex.Message);
    }

    [Fact]
    public void FromSheet_OutOfRangeIndex_ThrowsExcelException()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["Sheet1"] = [["Name", "Value"], ["A", "1"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var ex = Assert.Throws<ExcelException>(() =>
            HeroParser.Excel.Read<NameValue>().FromSheet(99).FromStream(xlsx));

        Assert.Contains("99", ex.Message);
    }
}

[GenerateBinder]
public class NameValue
{
    [TabularMap(Name = "Name")]
    public string Name { get; set; } = "";

    [TabularMap(Name = "Value")]
    public string Value { get; set; } = "";
}
