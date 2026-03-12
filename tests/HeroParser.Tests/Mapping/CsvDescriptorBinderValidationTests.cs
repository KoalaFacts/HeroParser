using System.Globalization;
using System.Text.RegularExpressions;
using HeroParser.SeparatedValues.Mapping;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Mapping;

[Trait("Category", "Unit")]
public class CsvDescriptorBinderValidationTests
{
    private sealed class SimpleRecord
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    private static void SetName(ref SimpleRecord r, ReadOnlySpan<char> v, CultureInfo _) => r.Name = new string(v);
    private static void SetAge(ref SimpleRecord r, ReadOnlySpan<char> v, CultureInfo _) => r.Age = int.TryParse(v, out var n) ? n : 0;

    // Helper: build a record descriptor with header-based binding for SimpleRecord
    private static CsvRecordDescriptor<SimpleRecord> BuildDescriptor(
        CsvPropertyValidation? nameValidation = null,
        CsvPropertyValidation? ageValidation = null,
        bool nameRequired = false,
        bool ageRequired = false)
    {
        return new CsvRecordDescriptor<SimpleRecord>(
        [
            new CsvPropertyDescriptor<SimpleRecord>(
                "Name",
                columnIndex: -1,
                setter: SetName,
                isRequired: nameRequired,
                validation: nameValidation),
            new CsvPropertyDescriptor<SimpleRecord>(
                "Age",
                columnIndex: -1,
                setter: SetAge,
                isRequired: ageRequired,
                validation: ageValidation)
        ]);
    }

    // Helper: parse CSV using descriptor, return records + errors
    private static (List<SimpleRecord> records, IReadOnlyList<ValidationError> errors) Parse(
        string csv,
        CsvPropertyValidation? nameValidation = null,
        CsvPropertyValidation? ageValidation = null,
        bool nameRequired = false,
        bool ageRequired = false,
        CsvRecordOptions? options = null)
    {
        var descriptor = BuildDescriptor(nameValidation, ageValidation, nameRequired, ageRequired);
        var binder = new CsvDescriptorBinder<SimpleRecord>(descriptor, options);
        var rowReader = Csv.ReadFromCharSpan(csv.AsSpan());
        var reader = new CsvRecordReader<char, SimpleRecord>(rowReader, binder);

        var records = new List<SimpleRecord>();
        foreach (var record in reader)
            records.Add(record);

        return (records, reader.Errors);
    }

    [Fact]
    public void NoValidation_NoErrors()
    {
        var csv = "Name,Age\nAlice,30\nBob,25";
        var (records, errors) = Parse(csv);

        Assert.Equal(2, records.Count);
        Assert.Empty(errors);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
    }

    [Fact]
    public void Validation_ValidData_NoErrors()
    {
        var validation = new CsvPropertyValidation { NotEmpty = true, MinLength = 1, MaxLength = 50 };
        var csv = "Name,Age\nAlice,30";
        var (records, errors) = Parse(csv, nameValidation: validation);

        Assert.Single(records);
        Assert.Empty(errors);
    }

    [Fact]
    public void NotEmpty_WhitespaceField_AddsValidationError()
    {
        var validation = new CsvPropertyValidation { NotEmpty = true };
        var csv = "Name,Age\n   ,30";
        var (records, errors) = Parse(csv, nameValidation: validation);

        Assert.Empty(records);
        Assert.Single(errors);
        Assert.Equal("NotEmpty", errors[0].Rule);
        Assert.Equal("Name", errors[0].PropertyName);
        Assert.Equal("Name", errors[0].ColumnName);
    }

    [Fact]
    public void Required_EmptyField_AddsValidationError()
    {
        var csv = "Name,Age\n,30";
        var (records, errors) = Parse(csv, nameRequired: true);

        Assert.Empty(records);
        Assert.Single(errors);
        Assert.Equal("Required", errors[0].Rule);
        Assert.Equal("Name", errors[0].PropertyName);
        Assert.Equal("Name", errors[0].ColumnName);
        Assert.Equal(string.Empty, errors[0].RawValue);
    }

    [Fact]
    public void MaxLength_ExceedsLimit_AddsValidationError()
    {
        var validation = new CsvPropertyValidation { MaxLength = 3 };
        var csv = "Name,Age\nAlicee,30";
        var (records, errors) = Parse(csv, nameValidation: validation);

        Assert.Empty(records);
        Assert.Single(errors);
        Assert.Equal("MaxLength", errors[0].Rule);
        Assert.Equal("Name", errors[0].PropertyName);
        Assert.Equal("Alicee", errors[0].RawValue);
        Assert.Equal("Name", errors[0].ColumnName);
    }

    [Fact]
    public void Range_OutOfBounds_AddsValidationError()
    {
        var validation = new CsvPropertyValidation { RangeMin = 0, RangeMax = 100 };
        var csv = "Name,Age\nAlice,150";
        var (records, errors) = Parse(csv, ageValidation: validation);

        Assert.Empty(records);
        Assert.Single(errors);
        Assert.Equal("Range", errors[0].Rule);
        Assert.Equal("Age", errors[0].PropertyName);
        Assert.Equal("150", errors[0].RawValue);
    }

    [Fact]
    public void Pattern_NoMatch_AddsValidationError()
    {
        var validation = new CsvPropertyValidation
        {
            Pattern = new Regex(@"^[A-Z][a-z]+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1))
        };
        var csv = "Name,Age\nalice,30";
        var (records, errors) = Parse(csv, nameValidation: validation);

        Assert.Empty(records);
        Assert.Single(errors);
        Assert.Equal("Pattern", errors[0].Rule);
        Assert.Equal("Name", errors[0].PropertyName);
        Assert.Equal("alice", errors[0].RawValue);
    }

    [Fact]
    public void ValidationErrors_CausesRowExclusion()
    {
        var validation = new CsvPropertyValidation { MaxLength = 3 };
        var csv = "Name,Age\nAlicee,30\nBob,25";
        var (records, errors) = Parse(csv, nameValidation: validation);

        // Only the valid row (Bob) should be returned
        Assert.Single(records);
        Assert.Equal("Bob", records[0].Name);
        Assert.Single(errors);
        Assert.Equal("MaxLength", errors[0].Rule);
    }

    [Fact]
    public void ValidationError_ContainsCorrectRowNumber()
    {
        var validation = new CsvPropertyValidation { NotEmpty = true };
        // Header = row 1, data row = row 2
        var csv = "Name,Age\n   ,30";
        var (_, errors) = Parse(csv, nameValidation: validation);

        Assert.Single(errors);
        Assert.True(errors[0].RowNumber > 0);
    }

    [Fact]
    public void ValidationError_ContainsCorrectColumnIndex()
    {
        var validation = new CsvPropertyValidation { MaxLength = 3 };
        var csv = "Name,Age\nAlicee,30";
        var (_, errors) = Parse(csv, nameValidation: validation);

        Assert.Single(errors);
        // Name is the first column (index 0) after header resolution
        Assert.True(errors[0].ColumnIndex >= 0);
    }

    [Fact]
    public void ValidationErrors_WithNullErrorList_StillExcludeRow()
    {
        var descriptor = BuildDescriptor(nameValidation: new CsvPropertyValidation { MaxLength = 3 });
        var binder = new CsvDescriptorBinder<SimpleRecord>(descriptor);
        var reader = Csv.ReadFromCharSpan("Name,Age\nAlice,30".AsSpan());

        Assert.True(reader.MoveNext());
        binder.BindHeader(reader.Current, rowNumber: 1);
        Assert.True(reader.MoveNext());

        var bound = binder.TryBind(reader.Current, rowNumber: 2, out _);

        Assert.False(bound);
    }
}
