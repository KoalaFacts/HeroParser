using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using Xunit;

namespace HeroParser.Tests.Mapping;

[Trait("Category", "Unit")]
public class FixedWidthMapIntegrationTests
{
    /// <summary>
    /// Sample record type for integration testing.
    /// </summary>
    public class Record
    {
        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets the integer value.</summary>
        public int Value { get; set; }

        /// <summary>Gets or sets the decimal amount.</summary>
        public decimal Amount { get; set; }
    }

    [Fact]
    public void WithMap_FromText_ReadsRecords()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10))
           .Map(r => r.Value, c => c.Start(10).Length(5).PadChar(' ').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(15).Length(10).PadChar(' ').Alignment(FieldAlignment.Right));

        // "Alice     " + "  100" + "     50.25"
        const string text = "Alice       100     50.25\nBob         200    100.50\n";

        var result = FixedWidth.Read<Record>().WithMap(map).FromText(text);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("Alice", result.Records[0].Name);
        Assert.Equal(100, result.Records[0].Value);
        Assert.Equal(50.25m, result.Records[0].Amount);
        Assert.Equal("Bob", result.Records[1].Name);
        Assert.Equal(200, result.Records[1].Value);
        Assert.Equal(100.50m, result.Records[1].Amount);
    }

    [Fact]
    public void WithMap_FromText_ValidationErrors_Collected()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10).NotEmpty())
           .Map(r => r.Value, c => c.Start(10).Length(5))
           .Map(r => r.Amount, c => c.Start(15).Length(10));

        // Row 1 has empty name (all spaces) => validation error
        const string text = "            100     50.25\nBob         200    100.50\n";

        var result = FixedWidth.Read<Record>().WithMap(map).FromText(text);

        Assert.Single(result.Records);
        Assert.Equal("Bob", result.Records[0].Name);
        Assert.True(result.Errors.Count >= 1);
    }

    [Fact]
    public void WithMap_FromText_RequiredErrors_Collected()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10).Required())
           .Map(r => r.Value, c => c.Start(10).Length(5).PadChar(' ').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(15).Length(10).PadChar(' ').Alignment(FieldAlignment.Right));

        var text = new string(' ', 10) + "100".PadLeft(5) + "50.25".PadLeft(10);

        var result = FixedWidth.Read<Record>().WithMap(map).FromText(text);

        Assert.Empty(result.Records);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Required", error.Rule);
        Assert.Equal("Name", error.PropertyName);
        Assert.Null(error.ColumnName);
        Assert.Equal(0, error.ColumnIndex);
    }

    [Fact]
    public void WithMap_RangeValidation_UsesConfiguredCulture()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10))
           .Map(r => r.Value, c => c.Start(10).Length(5).PadChar(' ').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(15).Length(10).PadChar(' ').Alignment(FieldAlignment.Right).Range(100, 200));

        var text = "Alice".PadRight(10) + "100".PadLeft(5) + "1,25".PadLeft(10);

        var result = FixedWidth.Read<Record>()
            .WithMap(map)
            .WithCulture("de-DE")
            .FromText(text);

        Assert.Empty(result.Records);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Range", error.Rule);
        Assert.Equal("Amount", error.PropertyName);
        Assert.Equal("1,25", error.RawValue);
    }

    [Fact]
    public void WithMap_ToText_WritesRecords()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10))
           .Map(r => r.Value, c => c.Start(10).Length(5).PadChar(' ').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(15).Length(10).PadChar(' ').Alignment(FieldAlignment.Right));

        var records = new List<Record>
        {
            new() { Name = "Alice", Value = 100, Amount = 50.25m }
        };

        var text = FixedWidth.Write<Record>().WithMap(map).ToText(records);

        Assert.Contains("Alice", text);
        Assert.Contains("100", text);
        Assert.Contains("50.25", text);
    }

    [Fact]
    public void WithMap_RoundTrip()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10))
           .Map(r => r.Value, c => c.Start(10).Length(5).PadChar(' ').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(15).Length(10).PadChar(' ').Alignment(FieldAlignment.Right));

        var original = new List<Record>
        {
            new() { Name = "Alice", Value = 100, Amount = 50.25m },
            new() { Name = "Bob", Value = 200, Amount = 100.50m }
        };

        var text = FixedWidth.Write<Record>().WithMap(map).ToText(original);
        var result = FixedWidth.Read<Record>().WithMap(map).FromText(text);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("Alice", result.Records[0].Name);
        Assert.Equal(100, result.Records[0].Value);
        Assert.Equal(50.25m, result.Records[0].Amount);
        Assert.Equal("Bob", result.Records[1].Name);
    }

    /// <summary>
    /// Subclass map for testing inheritance-based map configuration.
    /// </summary>
    public class RecordMap : FixedWidthMap<Record>
    {
        /// <summary>
        /// Initializes a new instance of <see cref="RecordMap"/> with default column mappings.
        /// </summary>
        public RecordMap()
        {
            Map(r => r.Name, c => c.Start(0).Length(10));
            Map(r => r.Value, c => c.Start(10).Length(5));
            Map(r => r.Amount, c => c.Start(15).Length(10));
        }
    }

    [Fact]
    public void WithMap_SubclassMap_ReadsRecords()
    {
        const string text = "Alice       100     50.25\n";
        var result = FixedWidth.Read<Record>().WithMap(new RecordMap()).FromText(text);

        Assert.Single(result.Records);
        Assert.Equal("Alice", result.Records[0].Name);
    }

    [Fact]
    public void ForEachFromText_WithMap_ThrowsNotSupported()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10))
           .Map(r => r.Value, c => c.Start(10).Length(5))
           .Map(r => r.Amount, c => c.Start(15).Length(10));

        var builder = FixedWidth.Read<Record>().WithMap(map);

        Assert.Throws<NotSupportedException>(() =>
            builder.ForEachFromText("test", _ => { }));
    }
}
