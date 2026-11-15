using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

/// <summary>
/// Numeric cells are stored in invariant notation in .xlsx; reading with a culture must bind the stored value.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class ExcelReadCultureTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Read_NumericCells_WithCulture_BindsStoredValue(string cultureName)
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "1234.5", "-100"],
            ["Gadget", "-0.00001", "7"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().WithCulture(cultureName).FromStream(xlsx);

        Assert.Equal(2, records.Count);
        Assert.Equal(1234.5m, records[0].Price);
        Assert.Equal(-100, records[0].Quantity);
        Assert.Equal(-0.00001m, records[1].Price);
        Assert.Equal(7, records[1].Quantity);
    }

    [Fact]
    public void Read_TextCells_WithCulture_StillParseInThatCulture()
    {
        // "9,99" is not numeric in invariant notation, so the helper stores it as a text cell.
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9,99", "3"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().WithCulture("de-DE").FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal(9.99m, records[0].Price);
    }

    [Fact]
    public void WriteThenRead_WithSameNonInvariantCulture_RoundTripsNumbers()
    {
        var input = new List<SimpleProduct>
        {
            new() { Name = "Widget", Price = 1234.56m, Quantity = 1500 },
        };

        var bytes = HeroParser.Excel.Write<SimpleProduct>().WithCulture("de-DE").ToBytes(input);
        using var ms = new MemoryStream(bytes);
        var records = HeroParser.Excel.Read<SimpleProduct>().WithCulture("de-DE").FromStream(ms);

        Assert.Single(records);
        Assert.Equal(1234.56m, records[0].Price);
        Assert.Equal(1500, records[0].Quantity);
    }

    [Fact]
    public void Read_AllSheets_WithCulture_BindsStoredValue()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx(new Dictionary<string, string[][]>
        {
            ["A"] = [["Name", "Price", "Quantity"], ["Widget", "2.75", "1"]],
            ["B"] = [["Name", "Price", "Quantity"], ["Gadget", "1000.5", "2"]],
        });

        var result = HeroParser.Excel.Read<SimpleProduct>().WithCulture("de-DE").AllSheets().FromStream(xlsx);

        Assert.Equal(2.75m, result["A"][0].Price);
        Assert.Equal(1000.5m, result["B"][0].Price);
    }

    [Fact]
    public async Task ReadAsync_NumericCells_WithCulture_BindsStoredValue()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "1234.5", "1"]
        ]);

        var records = new List<SimpleProduct>();
        await foreach (var record in HeroParser.Excel.Read<SimpleProduct>().WithCulture("de-DE")
            .FromStreamAsync(xlsx, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Single(records);
        Assert.Equal(1234.5m, records[0].Price);
    }
}
