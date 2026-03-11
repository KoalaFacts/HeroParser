using HeroParser.SeparatedValues.Mapping;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Mapping;

[Trait("Category", "Unit")]
public class CsvMapTests
{
    public class Trade
    {
        public string Symbol { get; set; } = "";
        public decimal Price { get; set; }
        public DateTime Date { get; set; }
    }

    [Fact]
    public void Map_WithName_BuildsDescriptor_WithHeaderBinding()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Ticker"))
           .Map(t => t.Price, c => c.Name("TradePrice"))
           .Map(t => t.Date, c => c.Name("TradeDate"));

        var descriptor = map.BuildReadDescriptor();

        Assert.True(descriptor.UsesHeaderBinding);
        Assert.Equal(3, descriptor.Properties.Length);
        Assert.Equal("Ticker", descriptor.Properties[0].Name);
        Assert.Equal("TradePrice", descriptor.Properties[1].Name);
        Assert.Equal("TradeDate", descriptor.Properties[2].Name);
    }

    [Fact]
    public void Map_WithIndex_BuildsDescriptor_WithIndexBinding()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Index(0))
           .Map(t => t.Price, c => c.Index(1))
           .Map(t => t.Date, c => c.Index(2));

        var descriptor = map.BuildReadDescriptor();

        Assert.False(descriptor.UsesHeaderBinding);
        Assert.Equal(0, descriptor.Properties[0].ColumnIndex);
        Assert.Equal(1, descriptor.Properties[1].ColumnIndex);
        Assert.Equal(2, descriptor.Properties[2].ColumnIndex);
    }

    [Fact]
    public void Map_WithValidation_BuildsDescriptor_WithValidationRules()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol").NotEmpty().MaxLength(10))
           .Map(t => t.Price, c => c.Name("Price").Range(0, 1_000_000));

        var descriptor = map.BuildReadDescriptor();

        Assert.NotNull(descriptor.Properties[0].Validation);
        Assert.True(descriptor.Properties[0].Validation!.NotEmpty);
        Assert.Equal(10, descriptor.Properties[0].Validation!.MaxLength);

        Assert.NotNull(descriptor.Properties[1].Validation);
        Assert.Equal(0, descriptor.Properties[1].Validation!.RangeMin);
        Assert.Equal(1_000_000, descriptor.Properties[1].Validation!.RangeMax);
    }

    [Fact]
    public void Map_WithFormat_BuildsWriteTemplates_WithFormat()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol"))
           .Map(t => t.Price, c => c.Name("Price").Format("F2"))
           .Map(t => t.Date, c => c.Name("Date").Format("yyyy-MM-dd"));

        var templates = map.BuildWriteTemplates();

        Assert.Equal(3, templates.Length);
        Assert.Null(templates[0].Format);
        Assert.Equal("F2", templates[1].Format);
        Assert.Equal("yyyy-MM-dd", templates[2].Format);
    }

    [Fact]
    public void Map_ThrowsOnDuplicateProperty()
    {
        var map = new CsvMap<Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            map.Map(t => t.Symbol, c => c.Name("Ticker")));

        Assert.Contains("Symbol", ex.Message);
    }

    [Fact]
    public void Map_FluentChaining_ReturnsThis()
    {
        var map = new CsvMap<Trade>();

        var result = map
            .Map(t => t.Symbol)
            .Map(t => t.Price)
            .Map(t => t.Date);

        Assert.Same(map, result);
    }

    public class TradeMap : CsvMap<Trade>
    {
        public TradeMap()
        {
            Map(t => t.Symbol, c => c.Name("Ticker"));
            Map(t => t.Price, c => c.Name("TradePrice").Format("F2"));
            Map(t => t.Date, c => c.Name("TradeDate").Format("yyyy-MM-dd"));
        }
    }

    [Fact]
    public void Map_Subclass_WorksLikeInline()
    {
        var map = new TradeMap();

        var descriptor = map.BuildReadDescriptor();
        Assert.True(descriptor.UsesHeaderBinding);
        Assert.Equal(3, descriptor.Properties.Length);
        Assert.Equal("Ticker", descriptor.Properties[0].Name);

        var templates = map.BuildWriteTemplates();
        Assert.Equal(3, templates.Length);
        Assert.Equal("F2", templates[1].Format);
    }
}
