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
    public void Read_NumericHeaderCell_WithCulture_StillMatchesMappedName()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "1.5"],
            ["Widget", "2.25"]
        ]);

        var records = HeroParser.Excel.Read<ExcelCultureNumericHeaderRecord>().WithCulture("de-DE").FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal(2.25m, records[0].Rate);
    }

    [Theory]
    [InlineData("1E-30", 1E-30)]
    [InlineData("1.2345678901234567E-20", 1.2345678901234567E-20)]
    [InlineData("1.7976931348623157E+308", double.MaxValue)]
    public void Read_FloatingPointCells_WithCulture_KeepStoredPrecision(string stored, double expected)
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Value"],
            [stored]
        ]);

        var records = HeroParser.Excel.Read<ExcelCultureDoubleRecord>().WithCulture("de-DE").FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal(expected, records[0].Value);
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

/// <summary>
/// Date-formatted cells are passed to the binder as ISO 8601 text; it must read them on every culture, non-Gregorian ones included.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class ExcelReadDateCultureTests
{
    [Theory]
    [InlineData("th-TH")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void Read_DateCells_WithCulture_BindsStoredDate(string cultureName)
    {
        using var xlsx = ExcelTestHelper.CreateXlsxWithDates("Sheet1", ["Date", "Day"], [[(44927.5, 1), (44927.0, 1)]]);

        var records = HeroParser.Excel.Read<ExcelCultureDateRecord>().WithCulture(cultureName).FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal(new DateTime(2023, 1, 1, 12, 0, 0), records[0].Date);
        Assert.Equal(new DateOnly(2023, 1, 1), records[0].Day);
    }
}

[GenerateBinder]
public class ExcelCultureDateRecord
{
    public DateTime Date { get; set; }
    public DateOnly Day { get; set; }
}

[GenerateBinder]
public class ExcelCultureNumericHeaderRecord
{
    public string Name { get; set; } = "";

    [TabularMap(Name = "1.5")]
    public decimal Rate { get; set; }
}

[GenerateBinder]
public class ExcelCultureDoubleRecord
{
    public double Value { get; set; }
}
