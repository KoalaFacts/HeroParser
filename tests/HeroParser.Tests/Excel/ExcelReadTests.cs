using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelReadTests
{
    [Fact]
    public void Read_SimpleRecords_FromStream()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"],
            ["Gadget", "24.95", "50"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().FromStream(xlsx);

        Assert.Equal(2, records.Count);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal(9.99m, records[0].Price);
        Assert.Equal(100, records[0].Quantity);
        Assert.Equal("Gadget", records[1].Name);
        Assert.Equal(24.95m, records[1].Price);
        Assert.Equal(50, records[1].Quantity);
    }

    [Fact]
    public void Read_WithMaxRows_LimitsResult()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["A", "1", "1"],
            ["B", "2", "2"],
            ["C", "3", "3"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().WithMaxRows(2).FromStream(xlsx);
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void Read_WithSkipRows_SkipsInitialRows()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Comment row to skip"],
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().SkipRows(1).FromStream(xlsx);
        Assert.Single(records);
        Assert.Equal("Widget", records[0].Name);
    }

    [Fact]
    public void Read_EmptyWorksheet_ReturnsEmptyList()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", []);

        var records = HeroParser.Excel.Read<SimpleProduct>().FromStream(xlsx);
        Assert.Empty(records);
    }

    [Fact]
    public void Read_HeaderOnly_ReturnsEmptyList()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().FromStream(xlsx);
        Assert.Empty(records);
    }

    [Fact]
    public void DeserializeRecords_FromStream_ReturnsTypedRecords()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"],
        ]);

        var records = HeroParser.Excel.DeserializeRecords<SimpleProduct>(xlsx);

        Assert.Single(records);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal(9.99m, records[0].Price);
        Assert.Equal(100, records[0].Quantity);
    }
}

[GenerateBinder]
public class SimpleProduct
{
    [TabularMap(Name = "Name")]
    public string Name { get; set; } = "";

    [TabularMap(Name = "Price")]
    public decimal Price { get; set; }

    [TabularMap(Name = "Quantity")]
    public int Quantity { get; set; }
}
