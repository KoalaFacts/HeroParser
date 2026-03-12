using HeroParser.SeparatedValues.Mapping;
using HeroParser.SeparatedValues.Reading.Records;
using Xunit;

namespace HeroParser.Tests.Mapping;

[Trait("Category", "Unit")]
public class CsvMapIntegrationTests
{
    public class Trade
    {
        public string Symbol { get; set; } = "";
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    [Fact]
    public void WithMap_FromText_ReadsRecords_ByHeaderName()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Ticker"))
           .Map(t => t.Price, c => c.Name("TradePrice"))
           .Map(t => t.Quantity, c => c.Name("Qty"));

        const string csv = "Ticker,TradePrice,Qty\nAAPL,150.50,100\nMSFT,320.75,200\n";

        var reader = Csv.Read<Trade>().WithMap(map).FromText(csv);
        var records = Collect(reader);

        Assert.Equal(2, records.Count);
        Assert.Equal("AAPL", records[0].Symbol);
        Assert.Equal(150.50m, records[0].Price);
        Assert.Equal(100, records[0].Quantity);
        Assert.Equal("MSFT", records[1].Symbol);
        Assert.Equal(320.75m, records[1].Price);
        Assert.Equal(200, records[1].Quantity);
    }

    [Fact]
    public void WithMap_FromText_ReadsRecords_ByIndex()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Index(0))
           .Map(t => t.Price, c => c.Index(1))
           .Map(t => t.Quantity, c => c.Index(2));

        const string csv = "AAPL,150.50,100\nMSFT,320.75,200\n";

        var reader = Csv.Read<Trade>().WithMap(map).WithoutHeader().FromText(csv);
        var records = Collect(reader);

        Assert.Equal(2, records.Count);
        Assert.Equal("AAPL", records[0].Symbol);
        Assert.Equal(150.50m, records[0].Price);
        Assert.Equal(100, records[0].Quantity);
    }

    [Fact]
    public void WithMap_FromText_ValidationErrors_Collected()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol").NotEmpty().MaxLength(3))
           .Map(t => t.Price, c => c.Name("Price"))
           .Map(t => t.Quantity, c => c.Name("Quantity"));

        // "AAPL" (4 chars) exceeds MaxLength(3) => validation error, row skipped
        // "IBM" (3 chars) is valid => included in output
        // empty symbol => NotEmpty violation, row skipped
        const string csv = "Symbol,Price,Quantity\nAAPL,150.50,100\nIBM,120.00,50\n,320.75,200\n";

        var reader = Csv.Read<Trade>().WithMap(map).FromText(csv);
        var records = new List<Trade>();
        foreach (var record in reader)
        {
            records.Add(record);
        }

        // Only the valid row (IBM) is returned; AAPL and empty are skipped due to validation errors
        Assert.Single(records);
        Assert.Equal("IBM", records[0].Symbol);
        Assert.True(reader.Errors.Count >= 2, $"Expected at least 2 validation errors, got {reader.Errors.Count}.");
    }

    [Fact]
    public void WithMap_FromText_RequiredErrors_Collected()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol").Required())
           .Map(t => t.Price, c => c.Name("Price"))
           .Map(t => t.Quantity, c => c.Name("Quantity"));

        const string csv = "Symbol,Price,Quantity\n,150.50,100\n";

        var reader = Csv.Read<Trade>().WithMap(map).FromText(csv);
        var records = Collect(reader);

        Assert.Empty(records);
        var error = Assert.Single(reader.Errors);
        Assert.Equal("Required", error.Rule);
        Assert.Equal("Symbol", error.ColumnName);
        Assert.Equal("Symbol", error.PropertyName);
        Assert.Equal(string.Empty, error.RawValue);
    }

    [Fact]
    public void WithMap_RangeValidation_UsesConfiguredCulture()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol"))
           .Map(t => t.Price, c => c.Name("Price").Range(100, 200))
           .Map(t => t.Quantity, c => c.Name("Quantity"));

        const string csv = "Symbol;Price;Quantity\nIBM;1,25;50\n";

        var reader = Csv.Read<Trade>()
            .WithMap(map)
            .WithDelimiter(';')
            .WithCulture("de-DE")
            .FromText(csv);
        var records = Collect(reader);

        Assert.Empty(records);
        var error = Assert.Single(reader.Errors);
        Assert.Equal("Range", error.Rule);
        Assert.Equal("Price", error.PropertyName);
        Assert.Equal("1,25", error.RawValue);
    }

    [Fact]
    public void InlineMap_FromText_ReadsRecords()
    {
        const string csv = "Symbol,Price,Quantity\nGOOG,2800.00,50\n";

        var reader = Csv.Read<Trade>()
            .Map(t => t.Symbol, c => c.Name("Symbol"))
            .Map(t => t.Price, c => c.Name("Price"))
            .Map(t => t.Quantity, c => c.Name("Quantity"))
            .FromText(csv);
        var records = Collect(reader);

        Assert.Single(records);
        Assert.Equal("GOOG", records[0].Symbol);
        Assert.Equal(2800.00m, records[0].Price);
        Assert.Equal(50, records[0].Quantity);
    }

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
        const string csv = "Ticker,TradePrice,Qty\nTSLA,700.00,30\n";

        var reader = Csv.Read<Trade>().WithMap(new TradeMap()).FromText(csv);
        var records = Collect(reader);

        Assert.Single(records);
        Assert.Equal("TSLA", records[0].Symbol);
        Assert.Equal(700.00m, records[0].Price);
        Assert.Equal(30, records[0].Quantity);
    }

    [Fact]
    public void WithMap_FromFile_ReadsRecords()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol"))
           .Map(t => t.Price, c => c.Name("Price"))
           .Map(t => t.Quantity, c => c.Name("Quantity"));

        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, "Symbol,Price,Quantity\nAMZN,3300.00,10\n");

            var reader = Csv.Read<Trade>().WithMap(map).FromFile(tempPath);
            var records = Collect(reader);

            Assert.Single(records);
            Assert.Equal("AMZN", records[0].Symbol);
            Assert.Equal(3300.00m, records[0].Price);
            Assert.Equal(10, records[0].Quantity);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void ByteOverload_ThrowsNotSupported_WithMap()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol"))
           .Map(t => t.Price, c => c.Name("Price"))
           .Map(t => t.Quantity, c => c.Name("Quantity"));

        const string csv = "Symbol,Price,Quantity\nAAPL,150.50,100\n";

        var builder = Csv.Read<Trade>().WithMap(map);

        Assert.Throws<NotSupportedException>(() => builder.FromText(csv, out _));
    }

    [Fact]
    public void WithMap_ToText_WritesRecords_WithHeaderNames()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Ticker"))
           .Map(t => t.Price, c => c.Name("TradePrice"))
           .Map(t => t.Quantity, c => c.Name("Qty"));

        var trades = new List<Trade>
        {
            new() { Symbol = "AAPL", Price = 150.50m, Quantity = 100 },
            new() { Symbol = "MSFT", Price = 320.75m, Quantity = 200 }
        };

        var csv = Csv.Write<Trade>().WithMap(map).ToText(trades);

        Assert.Contains("Ticker", csv);
        Assert.Contains("TradePrice", csv);
        Assert.Contains("Qty", csv);
        Assert.Contains("AAPL", csv);
        Assert.Contains("150.50", csv);
        Assert.Contains("100", csv);
        Assert.Contains("MSFT", csv);
        Assert.Contains("320.75", csv);
        Assert.Contains("200", csv);
    }

    [Fact]
    public void WithMap_RoundTrip_ReadThenWrite()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Ticker"))
           .Map(t => t.Price, c => c.Name("TradePrice"))
           .Map(t => t.Quantity, c => c.Name("Qty"));

        const string originalCsv = "Ticker,TradePrice,Qty\r\nAAPL,150.50,100\r\nMSFT,320.75,200\r\n";

        // Read
        var reader = Csv.Read<Trade>().WithMap(map).FromText(originalCsv);
        var records = new List<Trade>();
        foreach (var record in reader)
            records.Add(record);

        // Write
        var writtenCsv = Csv.Write<Trade>().WithMap(map).ToText(records);

        Assert.Equal(originalCsv, writtenCsv);
    }

    private static List<T> Collect<TElement, T>(CsvRecordReader<TElement, T> reader)
        where TElement : unmanaged, IEquatable<TElement>
        where T : new()
    {
        var list = new List<T>();
        foreach (var record in reader)
        {
            list.Add(record);
        }
        return list;
    }
}
