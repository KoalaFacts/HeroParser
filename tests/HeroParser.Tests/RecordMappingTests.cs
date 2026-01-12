using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Shared;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

// Run tests with writer/async operations sequentially to avoid ArrayPool race conditions
[Collection("AsyncWriterTests")]
public class RecordMappingTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void HeaderBasedMapping_BindsProperties()
    {
        var csv = "Name,Age\nJane,42\nBob,25";

        var reader = Csv.DeserializeRecords<Person>(csv);
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

        var reader = Csv.DeserializeRecords<Positioned>(csv, options);
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

        var reader = Csv.DeserializeRecords<NullableSample>(csv, options);
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
            var reader = Csv.DeserializeRecords<MissingColumnType>(csv);
            reader.MoveNext();
        });
    }

    [CsvGenerateBinder]
    internal sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    [CsvGenerateBinder]
    internal sealed class Positioned
    {
        [CsvColumn(Index = 1)]
        public string Value { get; set; } = string.Empty;

        [CsvColumn(Index = 0)]
        public int Id { get; set; }
    }

    [CsvGenerateBinder]
    internal sealed class NullableSample
    {
        public string? Name { get; set; }
        public int? Age { get; set; }
        public DateOnly? Birthday { get; set; }
    }

    [CsvGenerateBinder]
    internal sealed class MissingColumnType
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    [CsvGenerateBinder]
    internal sealed class Player
    {
        public string Name { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    #region Format Attribute Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FormatAttribute_ParsesDateWithCustomFormat()
    {
        var csv = "Id,Date\n1,01/15/2024\n2,12/31/2023";

        var reader = Csv.DeserializeRecords<DateFormatted>(csv);
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

        var reader = Csv.DeserializeRecords<DateTimeFormatted>(csv);
        var results = new List<DateTimeFormatted>();
        foreach (var item in reader) results.Add(item);

        Assert.Single(results);
        Assert.Equal(new DateTime(2024, 1, 15, 14, 30, 0), results[0].Timestamp);
    }

    [CsvGenerateBinder]
    internal sealed class DateFormatted
    {
        public int Id { get; set; }

        [CsvColumn(Format = "MM/dd/yyyy")]
        public DateOnly Date { get; set; }
    }

    [CsvGenerateBinder]
    internal sealed class DateTimeFormatted
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

        var reader = Csv.DeserializeRecords<PriceItem>(csv, options);
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

        var reader = Csv.DeserializeRecords<DateItem>(csv, options);
        var results = new List<DateItem>();
        foreach (var item in reader) results.Add(item);

        Assert.Single(results);
        Assert.Equal(new DateOnly(2024, 1, 15), results[0].Date);
    }

    [CsvGenerateBinder]
    internal sealed class PriceItem
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    [CsvGenerateBinder]
    internal sealed class DateItem
    {
        public int Id { get; set; }
        public DateOnly Date { get; set; }
    }

    #endregion

    #region Byte-Based Record Reading Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DeserializeRecordsFromBytes_ParsesUtf8Data()
    {
        var csv = "Name,Age\nJane,42\nBob,25"u8;

        var reader = Csv.DeserializeRecordsFromBytes<Person>(csv);
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
    public void DeserializeRecordsFromBytes_ParsesNumericTypes()
    {
        var csv = "Id,Price,Quantity,Score\n1,99.99,100,3.14"u8;

        var reader = Csv.DeserializeRecordsFromBytes<NumericRecord>(csv);
        var results = new List<NumericRecord>();
        foreach (var item in reader)
        {
            results.Add(item);
        }

        Assert.Single(results);
        Assert.Equal(1, results[0].Id);
        Assert.Equal(99.99m, results[0].Price);
        Assert.Equal(100L, results[0].Quantity);
        Assert.Equal(3.14, results[0].Score, 0.001);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DeserializeRecordsFromBytes_ParsesDateTimeTypes()
    {
        // Note: Utf8Parser has limited DateTime format support
        // Use 'G' format (M/d/yyyy h:mm:ss tt) for compatibility
        var csv = "Id,Date,Timestamp\n1,2024-06-15,06/15/2024 10:30:00"u8;

        var reader = Csv.DeserializeRecordsFromBytes<DateTimeRecord>(csv);
        var results = new List<DateTimeRecord>();
        foreach (var item in reader)
        {
            results.Add(item);
        }

        Assert.Single(results);
        Assert.Equal(1, results[0].Id);
        Assert.Equal(new DateOnly(2024, 6, 15), results[0].Date);
        Assert.Equal(new DateTime(2024, 6, 15, 10, 30, 0), results[0].Timestamp);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DeserializeRecordsFromBytes_WithoutHeader_BindsByIndex()
    {
        var csv = "1,first\n2,second"u8;
        var options = new CsvRecordOptions { HasHeaderRow = false };

        var reader = Csv.DeserializeRecordsFromBytes<Positioned>(csv, options);
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

    [CsvGenerateBinder]
    internal sealed class NumericRecord
    {
        public int Id { get; set; }
        public decimal Price { get; set; }
        public long Quantity { get; set; }
        public double Score { get; set; }
    }

    [CsvGenerateBinder]
    internal sealed class DateTimeRecord
    {
        public int Id { get; set; }
        public DateOnly Date { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion

    #region Delimiter Tests (Char API Bug Fix)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharApi_WithSemicolonDelimiter_BindsCorrectly()
    {
        // Tests fix for CsvCharToByteBinderAdapter delimiter bug
        var csv = "Name;Age\nJane;42\nBob;25";

        var reader = Csv.Read<Person>()
            .WithDelimiter(';')
            .FromText(csv);

        var results = reader.ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("Jane", results[0].Name);
        Assert.Equal(42, results[0].Age);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal(25, results[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharApi_WithPipeDelimiter_BindsCorrectly()
    {
        // Tests fix for CsvCharToByteBinderAdapter delimiter bug
        var csv = "Name|Age\nJane|42\nBob|25";

        var reader = Csv.Read<Person>()
            .WithDelimiter('|')
            .FromText(csv);

        var results = reader.ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("Jane", results[0].Name);
        Assert.Equal(42, results[0].Age);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal(25, results[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharApi_WithTabDelimiter_BindsCorrectly()
    {
        // Tests fix for CsvCharToByteBinderAdapter delimiter bug
        var csv = "Name\tAge\nJane\t42\nBob\t25";

        var reader = Csv.Read<Person>()
            .WithDelimiter('\t')
            .FromText(csv);

        var results = reader.ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("Jane", results[0].Name);
        Assert.Equal(42, results[0].Age);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal(25, results[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharApi_WithSemicolonDelimiter_MultipleColumns_BindsCorrectly()
    {
        // Tests fix with more columns to ensure all delimiters are correct
        var csv = "Id;Name;Age;City\n1;Jane;42;NYC\n2;Bob;25;LA";

        var reader = Csv.Read<DetailedPerson>()
            .WithDelimiter(';')
            .FromText(csv);

        var results = reader.ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Id);
        Assert.Equal("Jane", results[0].Name);
        Assert.Equal(42, results[0].Age);
        Assert.Equal("NYC", results[0].City);
        Assert.Equal(2, results[1].Id);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal(25, results[1].Age);
        Assert.Equal("LA", results[1].City);
    }

    [CsvGenerateBinder]
    internal sealed class DetailedPerson
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public string City { get; set; } = string.Empty;
    }

    #endregion
}
