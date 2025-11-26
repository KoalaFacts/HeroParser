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

    #region Custom Converter Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CustomConverter_ParsesCustomType()
    {
        var csv = "Name,Price\nWidget,12.50";

        var options = new CsvRecordOptions()
            .RegisterConverter<Money>((value, culture, format, out result) =>
            {
                if (decimal.TryParse(value, System.Globalization.NumberStyles.Number, culture, out var amount))
                {
                    result = new Money(amount);
                    return true;
                }
                result = default;
                return false;
            });

        var reader = Csv.ParseRecords<Product>(csv, options);
        var results = new List<Product>();
        foreach (var item in reader) results.Add(item);

        Assert.Single(results);
        Assert.Equal("Widget", results[0].Name);
        Assert.Equal(12.50m, results[0].Price?.Amount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CustomConverter_HandlesNullableCustomType()
    {
        var csv = "Name,Price\nWidget,\nGadget,25.00";

        var options = new CsvRecordOptions()
            .RegisterConverter<Money>((value, culture, format, out result) =>
            {
                if (value.IsEmpty)
                {
                    result = default;
                    return false;
                }
                if (decimal.TryParse(value, System.Globalization.NumberStyles.Number, culture, out var amount))
                {
                    result = new Money(amount);
                    return true;
                }
                result = default;
                return false;
            });

        var reader = Csv.ParseRecords<Product>(csv, options);
        var results = new List<Product>();
        foreach (var item in reader) results.Add(item);

        Assert.Equal(2, results.Count);
        Assert.Null(results[0].Price);
        Assert.Equal(25.00m, results[1].Price?.Amount);
    }

    private record Money(decimal Amount);

    private sealed class Product
    {
        public string Name { get; set; } = string.Empty;
        public Money? Price { get; set; }
    }

    #endregion

    #region Format Attribute Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FormatAttribute_ParsesDateWithCustomFormat()
    {
        var csv = "Id,Date\n1,01/15/2024\n2,12/31/2023";

        var reader = Csv.ParseRecords<DateFormatted>(csv);
        var results = new List<DateFormatted>();
        foreach (var item in reader) results.Add(item);

        Assert.Equal(2, results.Count);
        Assert.Equal(new DateOnly(2024, 1, 15), results[0].Date);
        Assert.Equal(new DateOnly(2023, 12, 31), results[1].Date);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FormatAttribute_ParsesDateTimeWithCustomFormat()
    {
        var csv = "Id,Timestamp\n1,2024-01-15 14:30:00";

        var reader = Csv.ParseRecords<DateTimeFormatted>(csv);
        var results = new List<DateTimeFormatted>();
        foreach (var item in reader) results.Add(item);

        Assert.Single(results);
        Assert.Equal(new DateTime(2024, 1, 15, 14, 30, 0), results[0].Timestamp);
    }

    private sealed class DateFormatted
    {
        public int Id { get; set; }

        [CsvColumn(Format = "MM/dd/yyyy")]
        public DateOnly Date { get; set; }
    }

    private sealed class DateTimeFormatted
    {
        public int Id { get; set; }

        [CsvColumn(Format = "yyyy-MM-dd HH:mm:ss")]
        public DateTime Timestamp { get; set; }
    }

    #endregion

    #region Culture Option Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CultureOption_ParsesNumbersWithCulture()
    {
        // German format uses period as thousands separator, comma as decimal separator
        // "1.250" in German means 1250 (period is thousands separator)
        // In InvariantCulture, "1.250" would mean 1.25 (period is decimal separator)
        var csv = "Name,Price\nWidget,1.250";
        var options = new CsvRecordOptions
        {
            Culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE")
        };

        var reader = Csv.ParseRecords<PriceItem>(csv, options);
        var results = new List<PriceItem>();
        foreach (var item in reader) results.Add(item);

        Assert.Single(results);
        Assert.Equal(1250m, results[0].Price);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CultureOption_ParsesDateWithCulture()
    {
        // German format: day.month.year
        var csv = "Id,Date\n1,15.01.2024";
        var options = new CsvRecordOptions
        {
            Culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE")
        };

        var reader = Csv.ParseRecords<DateItem>(csv, options);
        var results = new List<DateItem>();
        foreach (var item in reader) results.Add(item);

        Assert.Single(results);
        Assert.Equal(new DateOnly(2024, 1, 15), results[0].Date);
    }

    private sealed class PriceItem
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    private sealed class DateItem
    {
        public int Id { get; set; }
        public DateOnly Date { get; set; }
    }

    #endregion
}
