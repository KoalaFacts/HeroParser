using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelRowReadTests
{
    [Fact]
    public void Read_Rows_SkipsHeaderByDefault()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age", "City"],
            ["Alice", "30", "Seattle"],
            ["Bob", "25", "Portland"]
        ]);

        var rows = HeroParser.Excel.Read().FromStream(xlsx);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0][0]);
        Assert.Equal("30", rows[0][1]);
        Assert.Equal("Seattle", rows[0][2]);
        Assert.Equal("Bob", rows[1][0]);
    }

    [Fact]
    public void Read_Rows_WithoutHeader_IncludesAllRows()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Alice", "30"]
        ]);

        var rows = HeroParser.Excel.Read().WithoutHeader().FromStream(xlsx);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Name", rows[0][0]);
        Assert.Equal("Age", rows[0][1]);
        Assert.Equal("Alice", rows[1][0]);
    }

    [Fact]
    public void Read_Rows_FromNamedSheet()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["People"] = [["Name"], ["Alice"]],
            ["Numbers"] = [["Value"], ["42"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var rows = HeroParser.Excel.Read().FromSheet("Numbers").FromStream(xlsx);

        Assert.Single(rows);
        Assert.Equal("42", rows[0][0]);
    }

    [Fact]
    public void Read_Rows_FromSheetByIndex()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["First"] = [["Header"], ["A"]],
            ["Second"] = [["Header"], ["B"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var rows = HeroParser.Excel.Read().FromSheet(1).FromStream(xlsx);

        Assert.Single(rows);
        Assert.Equal("B", rows[0][0]);
    }

    [Fact]
    public void Read_Rows_SkipRows()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Metadata row"],
            ["Another skip"],
            ["Name", "Value"],
            ["A", "1"]
        ]);

        var rows = HeroParser.Excel.Read().SkipRows(2).FromStream(xlsx);

        Assert.Single(rows);
        Assert.Equal("A", rows[0][0]);
        Assert.Equal("1", rows[0][1]);
    }

    [Fact]
    public void Read_Rows_EmptySheet_ReturnsEmpty()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", []);

        var rows = HeroParser.Excel.Read().FromStream(xlsx);
        Assert.Empty(rows);
    }

    [Fact]
    public void Read_Rows_HeaderOnly_ReturnsEmpty()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"]
        ]);

        var rows = HeroParser.Excel.Read().FromStream(xlsx);
        Assert.Empty(rows);
    }
}
