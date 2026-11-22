using System.Globalization;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using HeroParser.SeparatedValues.Mapping;
using HeroParser.Tests.Mapping;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Culture;

/// <summary>
/// Since E1 the binders accept the ASCII <c>-</c> under fa-IR, ar-SA and he-IL, whose own negative sign carries bidi marks.
/// The <c>[Validate]</c> / <c>.Range(...)</c> check must read the same value: if it parses only the culture's sign, a
/// negative value bound from <c>-5</c> is never range-checked and slips past <c>RangeMin = 0</c>.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class ValidationAsciiMinusCultureTests
{
    public static TheoryData<string> BidiMinusCultures => ["fa-IR", "ar-SA", "he-IL"];

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void PropertyValidationRunner_Span_AsciiMinusBelowMinimum_ReportsRange(string cultureName)
    {
        var errors = new List<ValidationError>();
        PropertyValidationRunner.Validate(
            "-5".AsSpan(), "Quantity", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: 0, rangeMax: 100, pattern: null,
            errors, CultureInfo.GetCultureInfo(cultureName));

        var error = Assert.Single(errors);
        Assert.Equal("Range", error.Rule);
        Assert.Equal("Field 'Quantity' value -5 is less than minimum 0.", error.Message);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void PropertyValidationRunner_String_AsciiMinusBelowMinimum_ReportsRange(string cultureName)
    {
        var errors = new List<ValidationError>();
        PropertyValidationRunner.Validate(
            "-5", "Quantity", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: 0, rangeMax: 100, pattern: null,
            errors, CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal("Range", Assert.Single(errors).Rule);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void PropertyValidationRunner_CultureNegativeSign_StillReportsRange(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var errors = new List<ValidationError>();
        PropertyValidationRunner.Validate(
            (culture.NumberFormat.NegativeSign + "5").AsSpan(), "Quantity", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: 0, rangeMax: 100, pattern: null,
            errors, culture);

        Assert.Equal("Range", Assert.Single(errors).Rule);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void PropertyValidationRunner_AsciiMinusInRange_NoError(string cultureName)
    {
        var errors = new List<ValidationError>();
        PropertyValidationRunner.Validate(
            "-5".AsSpan(), "Quantity", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: -10, rangeMax: 100, pattern: null,
            errors, CultureInfo.GetCultureInfo(cultureName));

        Assert.Empty(errors);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void CsvMap_Range_AsciiMinusBelowMinimum_RejectsRow(string cultureName)
    {
        var map = new CsvMap<CsvMapIntegrationTests.Trade>();
        map.Map(t => t.Symbol, c => c.Name("Symbol"))
           .Map(t => t.Price, c => c.Name("Price"))
           .Map(t => t.Quantity, c => c.Name("Quantity").Range(0, 100));

        var reader = HeroParser.Csv.Read<CsvMapIntegrationTests.Trade>()
            .WithMap(map)
            .WithCulture(cultureName)
            .FromText("Symbol,Price,Quantity\nIBM,1,-5\n");
        var records = new List<CsvMapIntegrationTests.Trade>();
        foreach (var record in reader)
            records.Add(record);

        Assert.Empty(records);
        var error = Assert.Single(reader.Errors);
        Assert.Equal("Range", error.Rule);
        Assert.Equal("-5", error.RawValue);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void FixedWidthMap_Range_AsciiMinusBelowMinimum_RejectsRow(string cultureName)
    {
        var map = new FixedWidthMap<FixedWidthMapIntegrationTests.Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10))
           .Map(r => r.Value, c => c.Start(10).Length(5).PadChar(' ').Alignment(FieldAlignment.Right).Range(0, 100));

        var result = FixedWidth.Read<FixedWidthMapIntegrationTests.Record>()
            .WithMap(map)
            .WithCulture(cultureName)
            .FromText("Alice".PadRight(10) + "-5".PadLeft(5));

        Assert.Empty(result.Records);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Range", error.Rule);
        Assert.Equal("-5", error.RawValue);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void WriteValidationRunner_StringAsciiMinusBelowMinimum_ReportsRange(string cultureName)
    {
        var rules = new WritePropertyValidation(
            NotNull: false, NotEmpty: false, MaxLength: null, MinLength: null,
            RangeMin: 0, RangeMax: 100, Pattern: null);
        var errors = new List<ValidationError>();

        WriteValidationRunner.Validate("-5", "Quantity", 1, 0, rules, errors, CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal("Range", Assert.Single(errors).Rule);
    }
}
