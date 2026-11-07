using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Validation;

public class WriteValidationRunnerTests
{
    private static WritePropertyValidation NoRules() =>
        new(NotNull: false, NotEmpty: false, MaxLength: null, MinLength: null,
            RangeMin: null, RangeMax: null, Pattern: null);

    private static List<ValidationError> Run(
        object? value,
        WritePropertyValidation rules,
        string propertyName = "TestProp",
        int rowNumber = 1,
        int columnIndex = 0,
        CultureInfo? culture = null)
    {
        var errors = new List<ValidationError>();
        WriteValidationRunner.Validate(value, propertyName, rowNumber, columnIndex, rules, errors, culture ?? CultureInfo.InvariantCulture);
        return errors;
    }

    // ──────────────────────────────────────────────
    // NotNull
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_NullValue_AddsError()
    {
        var rules = NoRules() with { NotNull = true };

        var errors = Run(null, rules);

        Assert.Single(errors);
        Assert.Equal("NotNull", errors[0].Rule);
        Assert.Equal("TestProp", errors[0].PropertyName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_EmptyString_AddsError()
    {
        var rules = NoRules() with { NotNull = true };

        var errors = Run("", rules);

        Assert.Single(errors);
        Assert.Equal("NotNull", errors[0].Rule);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_ValidDecimal_NoError()
    {
        var rules = NoRules() with { NotNull = true };

        var errors = Run(42.5m, rules);

        Assert.Empty(errors);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_ZeroDecimal_NoError()
    {
        var rules = NoRules() with { NotNull = true };

        var errors = Run(0m, rules);

        Assert.Empty(errors);
    }

    // ──────────────────────────────────────────────
    // NotEmpty
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NotEmpty_WhitespaceString_AddsError()
    {
        var rules = NoRules() with { NotEmpty = true };

        var errors = Run("   ", rules);

        Assert.Single(errors);
        Assert.Equal("NotEmpty", errors[0].Rule);
    }

    // ──────────────────────────────────────────────
    // MaxLength
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void MaxLength_Exceeded_AddsError()
    {
        var rules = NoRules() with { MaxLength = 3 };

        var errors = Run("ABCD", rules);

        Assert.Single(errors);
        Assert.Equal("MaxLength", errors[0].Rule);
    }

    // ──────────────────────────────────────────────
    // Range
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_OutOfBounds_AddsError()
    {
        var rules = NoRules() with { RangeMin = 0, RangeMax = 100 };

        var errors = Run(-5m, rules);

        Assert.Single(errors);
        Assert.Equal("Range", errors[0].Rule);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void Range_StringValue_UsesWriterCultureNotCurrentCulture(string currentCulture)
    {
        // "2.75" is 2.75 in the writer's (invariant) culture; under de-DE's current culture
        // it would read as 275 and fail RangeMax = 100.
        var rules = NoRules() with { RangeMin = 0, RangeMax = 100 };
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(currentCulture);

            Assert.Empty(Run("2.75", rules));
            var errors = Run("275.5", rules);
            Assert.Single(errors);
            Assert.Equal("Range", errors[0].Rule);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_StringValue_HonoursConfiguredWriterCulture()
    {
        var rules = NoRules() with { RangeMin = 0, RangeMax = 100 };
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var german = CultureInfo.GetCultureInfo("de-DE");

            Assert.Empty(Run("2,75", rules, culture: german));
            Assert.Single(Run("275,5", rules, culture: german));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Length_NumericValue_UsesWriterCulture(string currentCulture)
    {
        // MinLength/MaxLength/Pattern check the value's text; it must be the writer
        // culture's text ("1.5"), not the current culture's ("1,5").
        var rules = new WritePropertyValidation(NotNull: false, NotEmpty: false, MaxLength: null, MinLength: null,
            RangeMin: null, RangeMax: null, Pattern: @"^\d+\.\d+$");
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(currentCulture);

            Assert.Empty(Run(1.5, rules));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static RangedTextRow[] Rows(string amount) => [new RangedTextRow { Amount = amount }];

    private sealed class RangedTextRow
    {
        [PositionalMap(Start = 0, Length = 8)]
        [Validate(RangeMin = 0, RangeMax = 100)]
        public string Amount { get; set; } = string.Empty;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void CsvWriter_StrictRange_UsesWriterCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var strict = new CsvWriteOptions { ValidationMode = ValidationMode.Strict };

            // Invariant writer culture: "2.75" is in range even though de-DE would read 275.
            string csv = Csv.WriteToText(Rows("2.75"), options: strict);
            Assert.Contains("2.75", csv);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var german = strict with { Culture = CultureInfo.GetCultureInfo("de-DE") };

            // de-DE writer culture: "2,75" is 2.75, while en-US would read 275.
            string germanCsv = Csv.WriteToText(Rows("2,75"), options: german);
            Assert.Contains("2,75", germanCsv);
            var ex = Assert.Throws<ValidationException>(() =>
                Csv.WriteToText(Rows("275,5"), options: german));
            Assert.Equal("Range", Assert.Single(ex.Errors).Rule);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void FixedWidthWriter_StrictRange_UsesWriterCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            string text = FixedWidth.WriteToText(Rows("2.75"));
            Assert.Contains("2.75", text);
            var ex = Assert.Throws<ValidationException>(() => FixedWidth.WriteToText(Rows("250.5")));
            Assert.Equal("Range", Assert.Single(ex.Errors).Rule);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void ExcelWriter_StrictRange_UsesWriterCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // Invariant writer culture: "2.75" is in range even though de-DE would read 275.
            Assert.NotEmpty(HeroParser.Excel.SerializeRecords(Rows("2.75")));
            var ex = Assert.Throws<ValidationException>(() => HeroParser.Excel.SerializeRecords(Rows("250.5")));
            Assert.Equal("Range", Assert.Single(ex.Errors).Rule);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ──────────────────────────────────────────────
    // No rules
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NoRulesConfigured_NoError()
    {
        var rules = NoRules();

        var errors = Run(null, rules);

        Assert.Empty(errors);
    }
}
