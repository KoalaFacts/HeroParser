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
        int columnIndex = 0)
    {
        var errors = new List<ValidationError>();
        WriteValidationRunner.Validate(value, propertyName, rowNumber, columnIndex, rules, errors);
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
