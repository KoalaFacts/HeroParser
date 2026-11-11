using HeroParser.JsonLines;
using HeroParser.SeparatedValues.Reading.Records;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// A <c>[GenerateBinder]</c> record with a nullable enum property must build and read through the
/// generated CSV, Fixed-Width and JSONL binders: a value, an empty field (null) and an unparseable value.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class GeneratedNullableEnumBindingTests
{
    public enum Level { Low, High }

    [GenerateBinder]
    public class Ticket
    {
        [TabularMap(Name = "Id")]
        [PositionalMap(Start = 0, Length = 3)]
        public int Id { get; set; }

        [TabularMap(Name = "Level")]
        [PositionalMap(Start = 3, Length = 5)]
        public Level? Level { get; set; }
    }

    [Fact]
    public void Csv_NullableEnum_ReadsValueAndEmptyAsNull()
    {
        using var reader = Csv.Read<Ticket>().FromText("Id,Level\n1,high\n2,\n");

        var records = reader.ToList();

        Assert.Equal(2, records.Count);
        Assert.Equal(Level.High, records[0].Level);
        Assert.Null(records[1].Level);
    }

    [Fact]
    public void Csv_NullableEnum_UnparseableValueIsNull_LikeOtherNullableColumns()
    {
        // The generated CSV binder leaves every nullable column null when its text does not parse
        // (the same rule as int?); this pins that a nullable enum follows it.
        using var reader = Csv.Read<Ticket>().FromText("Id,Level\n1,bogus\n");

        var record = Assert.Single(reader.ToList());

        Assert.Equal(1, record.Id);
        Assert.Null(record.Level);
    }

    [Fact]
    public void FixedWidth_NullableEnum_ReadsValueAndBlankAsNull()
    {
        var result = FixedWidth.Read<Ticket>().FromText("  1Low  \n  2     \n");

        Assert.Equal(2, result.Records.Count);
        Assert.Equal(Level.Low, result.Records[0].Level);
        Assert.Null(result.Records[1].Level);
    }

    [Fact]
    public void FixedWidth_NullableEnum_UnparseableValueThrows()
    {
        Assert.ThrowsAny<Exception>(() => FixedWidth.Read<Ticket>().FromText("  1bogus\n"));
    }

    [Fact]
    public void Jsonl_NullableEnum_ReadsStringNumberAndNull()
    {
        using var reader = Jsonl.Read<Ticket>().FromText(
            "{\"Id\":1,\"Level\":\"high\"}\n{\"Id\":2,\"Level\":0}\n{\"Id\":3,\"Level\":null}\n");

        var records = reader.ToList();

        Assert.Equal(3, records.Count);
        Assert.Equal(Level.High, records[0].Level);
        Assert.Equal(Level.Low, records[1].Level);
        Assert.Null(records[2].Level);
    }

    [Fact]
    public void Jsonl_NullableEnum_InvalidStringThrows()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            using var reader = Jsonl.Read<Ticket>().FromText("{\"Id\":1,\"Level\":\"bogus\"}\n");
            _ = reader.ToList();
        });
    }
}
