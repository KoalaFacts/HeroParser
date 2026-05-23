using HeroParser.Excels.Reading;
using HeroParser.SeparatedValues.Core;
using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelOnErrorTests
{
    // ──────────────────────────────────────────────
    // Happy path: OnError with SkipRecord
    // ──────────────────────────────────────────────

    [Fact]
    public void OnError_SkipRecord_SkipsRowWithParseFailure()
    {
        // Row 2 has "not-a-number" for Quantity which is int — will throw a parse exception
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"],
            ["Broken", "1.00", "not-a-number"],
            ["Gadget", "2.50", "50"]
        ]);

        var skippedRows = new List<int>();

        var records = HeroParser.Excel.Read<ErrorProduct>()
            .OnError((ctx, _) =>
            {
                skippedRows.Add(ctx.Row);
                return ExcelDeserializeErrorAction.SkipRecord;
            })
            .FromStream(xlsx);

        Assert.Equal(2, records.Count);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal("Gadget", records[1].Name);
        Assert.Single(skippedRows);
    }

    [Fact]
    public void OnError_SkipRecord_ErrorContextContainsSheetName()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("MySheet", [
            ["Name", "Price", "Quantity"],
            ["Bad", "1.00", "not-a-number"]
        ]);

        string? capturedSheet = null;

        HeroParser.Excel.Read<ErrorProduct>()
            .OnError((ctx, _) =>
            {
                capturedSheet = ctx.SheetName;
                return ExcelDeserializeErrorAction.SkipRecord;
            })
            .FromStream(xlsx);

        Assert.Equal("MySheet", capturedSheet);
    }

    [Fact]
    public void OnError_SkipRecord_ErrorContextContainsRowNumber()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Good", "1.00", "5"],
            ["Bad", "1.00", "not-a-number"]
        ]);

        int capturedRow = -1;

        HeroParser.Excel.Read<ErrorProduct>()
            .OnError((ctx, _) =>
            {
                capturedRow = ctx.Row;
                return ExcelDeserializeErrorAction.SkipRecord;
            })
            .FromStream(xlsx);

        // Row 3 is the bad row (1=header, 2=good, 3=bad)
        Assert.Equal(3, capturedRow);
    }

    // ──────────────────────────────────────────────
    // Edge case: all rows fail → empty result
    // ──────────────────────────────────────────────

    [Fact]
    public void OnError_SkipRecord_AllRowsFail_ReturnsEmptyList()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Bad1", "1.00", "not-a-number"],
            ["Bad2", "2.00", "also-bad"],
            ["Bad3", "3.00", "still-bad"]
        ]);

        var records = HeroParser.Excel.Read<ErrorProduct>()
            .OnError((_, _) => ExcelDeserializeErrorAction.SkipRecord)
            .FromStream(xlsx);

        Assert.Empty(records);
    }

    // ──────────────────────────────────────────────
    // Throw action: OnError with Throw rethrows
    // ──────────────────────────────────────────────

    [Fact]
    public void OnError_Throw_RethrowsException()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Bad", "1.00", "not-a-number"]
        ]);

        Assert.Throws<CsvException>(() =>
            HeroParser.Excel.Read<ErrorProduct>()
                .OnError((_, _) => ExcelDeserializeErrorAction.Throw)
                .FromStream(xlsx));
    }

    // ──────────────────────────────────────────────
    // No error handler: exceptions propagate normally
    // ──────────────────────────────────────────────

    [Fact]
    public void NoOnError_ParseFailure_ThrowsException()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Bad", "1.00", "not-a-number"]
        ]);

        Assert.Throws<CsvException>(() =>
            HeroParser.Excel.Read<ErrorProduct>().FromStream(xlsx));
    }
}

[GenerateBinder]
public class ErrorProduct
{
    [TabularMap(Name = "Name")]
    public string Name { get; set; } = "";

    [TabularMap(Name = "Price")]
    public decimal Price { get; set; }

    [TabularMap(Name = "Quantity")]
    public int Quantity { get; set; }
}
