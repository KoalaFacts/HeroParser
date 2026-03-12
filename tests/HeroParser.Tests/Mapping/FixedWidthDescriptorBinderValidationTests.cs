using System.Globalization;
using System.Text.RegularExpressions;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using HeroParser.FixedWidths.Records.Binding;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Mapping;

[Trait("Category", "Unit")]
public class FixedWidthDescriptorBinderValidationTests
{
    private sealed class FixedWidthRecord
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    // Name: positions 0-9 (length 10), Value: positions 10-14 (length 5)
    private static void NameSetter(ref FixedWidthRecord instance, ReadOnlySpan<char> value, CultureInfo culture)
        => instance.Name = new string(value);

    private static void ValueSetter(ref FixedWidthRecord instance, ReadOnlySpan<char> value, CultureInfo culture)
        => instance.Value = int.TryParse(value, out var n) ? n : 0;

    private static FixedWidthRecordDescriptor<FixedWidthRecord> BuildDescriptor(
        FixedWidthPropertyValidation? nameValidation = null,
        FixedWidthPropertyValidation? valueValidation = null)
    {
        return new FixedWidthRecordDescriptor<FixedWidthRecord>(
        [
            new FixedWidthPropertyDescriptor<FixedWidthRecord>(
                "Name",
                start: 0,
                length: 10,
                padChar: ' ',
                alignment: FieldAlignment.Left,
                setter: NameSetter,
                isRequired: false,
                validation: nameValidation),
            new FixedWidthPropertyDescriptor<FixedWidthRecord>(
                "Value",
                start: 10,
                length: 5,
                padChar: ' ',
                alignment: FieldAlignment.Left,
                setter: ValueSetter,
                isRequired: false,
                validation: valueValidation)
        ]);
    }

    // Helper: parse fixed-width text using descriptor binder, return records + errors
    private static (List<FixedWidthRecord> records, List<ValidationError> errors) Parse(
        string text,
        FixedWidthPropertyValidation? nameValidation = null,
        FixedWidthPropertyValidation? valueValidation = null)
    {
        var descriptor = BuildDescriptor(nameValidation, valueValidation);
        var binder = new FixedWidthDescriptorBinder<FixedWidthRecord>(descriptor);
        var errors = new List<ValidationError>();
        var records = new List<FixedWidthRecord>();

        var options = new FixedWidthReadOptions { AllowShortRows = true };
        foreach (var row in FixedWidth.ReadFromText(text, options))
        {
            if (binder.TryBind(row, out var record, errors))
                records.Add(record);
        }

        return (records, errors);
    }

    [Fact]
    public void NoValidation_AllRowsPass()
    {
        // 10-char name + 5-char value per row
        var text = "Alice     00042\nBob       00007";
        var (records, errors) = Parse(text);

        Assert.Equal(2, records.Count);
        Assert.Empty(errors);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(42, records[0].Value);
    }

    [Fact]
    public void ValidData_PassesValidation()
    {
        var nameValidation = new FixedWidthPropertyValidation { NotEmpty = true, MinLength = 1, MaxLength = 10 };
        var text = "Alice     00042";
        var (records, errors) = Parse(text, nameValidation: nameValidation);

        Assert.Single(records);
        Assert.Empty(errors);
        Assert.Equal("Alice", records[0].Name);
    }

    [Fact]
    public void Validation_NotEmpty_RejectsEmptyField()
    {
        var nameValidation = new FixedWidthPropertyValidation { NotEmpty = true };
        // Name field is all spaces — after trimming it becomes empty
        var text = "          00042";
        var (records, errors) = Parse(text, nameValidation: nameValidation);

        Assert.Empty(records);
        Assert.Single(errors);
        Assert.Equal("NotEmpty", errors[0].Rule);
        Assert.Equal("Name", errors[0].PropertyName);
    }

    [Fact]
    public void Validation_MaxLength_RejectsTooLong()
    {
        var nameValidation = new FixedWidthPropertyValidation { MaxLength = 3 };
        // "Alice" (5 chars) exceeds MaxLength of 3
        var text = "Alice     00042";
        var (records, errors) = Parse(text, nameValidation: nameValidation);

        Assert.Empty(records);
        Assert.Single(errors);
        Assert.Equal("MaxLength", errors[0].Rule);
        Assert.Equal("Name", errors[0].PropertyName);
    }

    [Fact]
    public void Validation_Range_RejectsOutOfRange()
    {
        var valueValidation = new FixedWidthPropertyValidation { RangeMin = 0, RangeMax = 10 };
        // Value "00042" parses to 42, which exceeds max of 10
        var text = "Alice     00042";
        var (records, errors) = Parse(text, valueValidation: valueValidation);

        Assert.Empty(records);
        Assert.Single(errors);
        Assert.Equal("Range", errors[0].Rule);
        Assert.Equal("Value", errors[0].PropertyName);
        Assert.Equal(10, errors[0].ColumnIndex);
        Assert.Null(errors[0].ColumnName);
    }

    [Fact]
    public void Validation_Pattern_RejectsNonMatch()
    {
        var nameValidation = new FixedWidthPropertyValidation
        {
            Pattern = new Regex(@"^[A-Z][a-z]+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1))
        };
        // "alice" doesn't match the pattern (requires capital first letter)
        var text = "alice     00042";
        var (records, errors) = Parse(text, nameValidation: nameValidation);

        Assert.Empty(records);
        Assert.Single(errors);
        Assert.Equal("Pattern", errors[0].Rule);
        Assert.Equal("Name", errors[0].PropertyName);
        Assert.Equal("alice", errors[0].RawValue);
    }

    [Fact]
    public void Validation_ReturnsErrors_AndExcludesRow()
    {
        var nameValidation = new FixedWidthPropertyValidation { MaxLength = 3 };
        // First row invalid (Alice = 5 chars), second row valid (Bob = 3 chars)
        var text = "Alice     00042\nBob       00007";
        var (records, errors) = Parse(text, nameValidation: nameValidation);

        Assert.Single(records);
        Assert.Equal("Bob", records[0].Name);
        Assert.Single(errors);
        Assert.Equal("MaxLength", errors[0].Rule);
    }

    [Fact]
    public void ValidationErrors_WithNullErrorList_StillExcludeRow()
    {
        var descriptor = BuildDescriptor(nameValidation: new FixedWidthPropertyValidation { MaxLength = 3 });
        var binder = new FixedWidthDescriptorBinder<FixedWidthRecord>(descriptor);
        using var reader = FixedWidth.ReadFromText("Alice     00042", new FixedWidthReadOptions { AllowShortRows = true });

        Assert.True(reader.MoveNext());

        var bound = binder.TryBind(reader.Current, out _);

        Assert.False(bound);
    }
}
