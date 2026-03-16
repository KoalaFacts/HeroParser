using HeroParser.SeparatedValues.Mapping;
using HeroParser.Tests.Fixtures.Excel;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelFluentMappingTests
{
    // Record without [GenerateBinder] — fluent mapping works independently of source generators.
    public class Trade
    {
        public string Symbol { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    // ──────────────────────────────────────────────
    // WithMap — by header name
    // ──────────────────────────────────────────────

    [Fact]
    public void WithMap_ByHeaderName_ReadsRecords()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Ticker"))
           .Map(t => t.Price, c => c.Name("TradePrice"))
           .Map(t => t.Quantity, c => c.Name("Qty"));

        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Ticker", "TradePrice", "Qty"],
            ["AAPL", "150.50", "100"],
            ["MSFT", "320.75", "200"]
        ]);

        var records = HeroParser.Excel.Read<Trade>().WithMap(map).FromStream(xlsx);

        Assert.Equal(2, records.Count);
        Assert.Equal("AAPL", records[0].Symbol);
        Assert.Equal(150.50m, records[0].Price);
        Assert.Equal(100, records[0].Quantity);
        Assert.Equal("MSFT", records[1].Symbol);
        Assert.Equal(320.75m, records[1].Price);
        Assert.Equal(200, records[1].Quantity);
    }

    // ──────────────────────────────────────────────
    // Inline Map<TProperty> — by property expression
    // ──────────────────────────────────────────────

    [Fact]
    public void InlineMap_ByPropertyExpression_ReadsRecords()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Ticker", "TradePrice", "Qty"],
            ["GOOG", "2800.00", "50"],
            ["TSLA", "700.00", "30"]
        ]);

        var records = HeroParser.Excel.Read<Trade>()
            .Map(t => t.Symbol, c => c.Name("Ticker"))
            .Map(t => t.Price, c => c.Name("TradePrice"))
            .Map(t => t.Quantity, c => c.Name("Qty"))
            .FromStream(xlsx);

        Assert.Equal(2, records.Count);
        Assert.Equal("GOOG", records[0].Symbol);
        Assert.Equal(2800.00m, records[0].Price);
        Assert.Equal(50, records[0].Quantity);
        Assert.Equal("TSLA", records[1].Symbol);
        Assert.Equal(700.00m, records[1].Price);
        Assert.Equal(30, records[1].Quantity);
    }

    // ──────────────────────────────────────────────
    // Subclass map pattern
    // ──────────────────────────────────────────────

    public class TradeMap : CsvMap<Trade>
    {
        public TradeMap()
        {
            Map(t => t.Symbol, c => c.Name("Ticker"));
            Map(t => t.Price, c => c.Name("TradePrice"));
            Map(t => t.Quantity, c => c.Name("Qty"));
        }
    }

    [Fact]
    public void WithMap_SubclassMap_ReadsRecords()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Ticker", "TradePrice", "Qty"],
            ["TSLA", "700.00", "30"]
        ]);

        var records = HeroParser.Excel.Read<Trade>().WithMap(new TradeMap()).FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal("TSLA", records[0].Symbol);
        Assert.Equal(700.00m, records[0].Price);
        Assert.Equal(30, records[0].Quantity);
    }

    // ──────────────────────────────────────────────
    // Validation with map + lenient mode
    // ──────────────────────────────────────────────

    [Fact]
    public void WithMap_LenientMode_MaxLengthViolation_SkipsInvalidRows_ValidRowsPassThrough()
    {
        // Symbol "ALPHABET" (8 chars) exceeds MaxLength(4) — row is skipped in lenient mode.
        // Symbol "IBM" (3 chars) is within MaxLength(4) — row is included.
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol").MaxLength(4))
           .Map(t => t.Price, c => c.Name("Price"))
           .Map(t => t.Quantity, c => c.Name("Qty"));

        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Symbol", "Price", "Qty"],
            ["ALPHABET", "100.00", "10"],
            ["IBM", "120.00", "50"]
        ]);

        var records = HeroParser.Excel.Read<Trade>()
            .WithMap(map)
            .WithValidationMode(ValidationMode.Lenient)
            .FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal("IBM", records[0].Symbol);
        Assert.Equal(120.00m, records[0].Price);
        Assert.Equal(50, records[0].Quantity);
    }

    // ──────────────────────────────────────────────
    // Map() after WithMap() throws
    // ──────────────────────────────────────────────

    [Fact]
    public void Map_AfterWithMap_ThrowsInvalidOperationException()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol"));

        var builder = HeroParser.Excel.Read<Trade>().WithMap(map);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Map(t => t.Price, c => c.Name("Price")));

        Assert.Contains("Cannot call Map() after WithMap()", ex.Message);
    }
}
