using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

public class RecordMappingTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void HeaderBasedMapping_BindsProperties()
    {
        var csv = "Name,Age\nJane,42\nBob,25";

        var reader = Csv.ParseRecords<Person>(csv);
        var results = new List<Person>();
        foreach (var person in reader)
        {
            results.Add(person);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("Jane", results[0].Name);
        Assert.Equal(42, results[0].Age);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal(25, results[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void IndexBasedMapping_WithoutHeader_BindsByIndex()
    {
        var csv = "1,first\n2,second";
        var options = new CsvRecordOptions { HasHeaderRow = false };

        var reader = Csv.ParseRecords<Positioned>(csv, options);
        var results = new List<Positioned>();
        foreach (var item in reader)
        {
            results.Add(item);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("first", results[0].Value);
        Assert.Equal(2, results[1].Id);
        Assert.Equal("second", results[1].Value);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NullableColumns_AllowEmptyFields()
    {
        var csv = "Name,Age,Birthday\nJane,42,2024-01-01\nBob,,";
        var options = new CsvRecordOptions { HasHeaderRow = true };

        var reader = Csv.ParseRecords<NullableSample>(csv, options);
        var results = new List<NullableSample>();
        foreach (var item in reader)
        {
            results.Add(item);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("Jane", results[0].Name);
        Assert.Equal(42, results[0].Age);
        Assert.Equal(new DateOnly(2024, 1, 1), results[0].Birthday);

        Assert.Equal("Bob", results[1].Name);
        Assert.Null(results[1].Age);
        Assert.Null(results[1].Birthday);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void MissingColumns_ThrowWhenNotAllowed()
    {
        var csv = "Name\nJane";

        Assert.Throws<CsvException>(() =>
        {
            var reader = Csv.ParseRecords<MissingColumnType>(csv);
            reader.MoveNext();
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task AsyncStreaming_MapsRecords()
    {
        var csv = "Name,Score\nAlice,9\nBob,7";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var results = new List<Player>();
        await foreach (var player in Csv.ParseRecordsAsync<Player>(stream, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(player);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal(9, results[0].Score);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal(7, results[1].Score);
    }
    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private sealed class Positioned
    {
        [CsvColumn(Index = 1)]
        public string Value { get; set; } = string.Empty;

        [CsvColumn(Index = 0)]
        public int Id { get; set; }
    }

    private sealed class NullableSample
    {
        public string? Name { get; set; }
        public int? Age { get; set; }
        public DateOnly? Birthday { get; set; }
    }

    private sealed class MissingColumnType
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private sealed class Player
    {
        public string Name { get; set; } = string.Empty;
        public int Score { get; set; }
    }
}
