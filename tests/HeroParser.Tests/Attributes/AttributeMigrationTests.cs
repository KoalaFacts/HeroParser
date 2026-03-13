using HeroParser.SeparatedValues.Reading.Records;
using Xunit;

namespace HeroParser.Tests.Attributes;

[GenerateBinder]
public class MigrationTestRecord
{
    [TabularMap(Name = "Name")]
    [Validate(NotEmpty = true, MaxLength = 50)]
    public string Name { get; set; } = "";

    [TabularMap(Name = "Value")]
    [Parse(Format = "N2")]
    [Validate(RangeMin = 0, RangeMax = 1000)]
    public decimal Value { get; set; }

    [TabularMap(Name = "Date")]
    [Parse(Format = "yyyy-MM-dd")]
    [Validate(NotNull = true)]
    public DateTime Date { get; set; }
}

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class AttributeMigrationTests
{
    [Fact]
    public void GeneratedBinder_WithUnifiedAttributes_ParsesCsv()
    {
        var csv = "Name,Value,Date\nWidget,42.50,2026-01-15";
        using var reader = Csv.Read<MigrationTestRecord>().FromText(csv);
        var records = reader.ToList();

        Assert.Single(records);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal(42.50m, records[0].Value);
        Assert.Equal(new DateTime(2026, 1, 15), records[0].Date);
    }
}
