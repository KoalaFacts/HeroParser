using System.Globalization;
using System.Text.RegularExpressions;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Direct tests for the internal <see cref="PropertyValidationRunner"/> used by both
/// CSV and FixedWidth descriptor binders. ~60 lines were uncovered (63%).
/// </summary>
[Trait("Category", "Unit")]
public class PropertyValidationRunnerTests
{
    private static readonly CultureInfo invariant = CultureInfo.InvariantCulture;

    // ───── Span overload ─────

    [Fact]
    public void Span_NotEmpty_FailsOnWhitespace()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "   ".AsSpan(), "Name", 1, 0, null,
            notEmpty: true, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
        Assert.Single(errors);
        Assert.Equal("NotEmpty", errors[0].Rule);
    }

    [Fact]
    public void Span_NotEmpty_FailsOnEmpty()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "".AsSpan(), "Name", 1, 0, null,
            notEmpty: true, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
    }

    [Fact]
    public void Span_NotEmpty_PassesOnContent()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "hello".AsSpan(), "Name", 1, 0, null,
            notEmpty: true, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.False(hasErrors);
        Assert.Empty(errors);
    }

    [Fact]
    public void Span_MinLength_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "ab".AsSpan(), "x", 1, 0, null,
            notEmpty: false, minLength: 5, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
        Assert.Equal("MinLength", errors[0].Rule);
    }

    [Fact]
    public void Span_MaxLength_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "abcdef".AsSpan(), "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: 3,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
        Assert.Equal("MaxLength", errors[0].Rule);
    }

    [Fact]
    public void Span_RangeMin_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "5".AsSpan(), "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: 10, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
        Assert.Equal("Range", errors[0].Rule);
    }

    [Fact]
    public void Span_RangeMax_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "100".AsSpan(), "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: 50, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
        Assert.Equal("Range", errors[0].Rule);
    }

    [Fact]
    public void Span_Range_Passes_WhenInBounds()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "25".AsSpan(), "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: 10, rangeMax: 100, pattern: null,
            errors, invariant);
        Assert.False(hasErrors);
    }

    [Fact]
    public void Span_Range_NonNumericValue_Skipped()
    {
        // When the value isn't parseable as numeric, range checks are silently skipped.
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "hello".AsSpan(), "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: 10, rangeMax: 100, pattern: null,
            errors, invariant);
        Assert.False(hasErrors);
    }

    [Fact]
    public void Span_Pattern_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "abc123".AsSpan(), "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null,
            pattern: new Regex(@"^[a-z]+$"),
            errors, invariant);
        Assert.True(hasErrors);
        Assert.Equal("Pattern", errors[0].Rule);
    }

    [Fact]
    public void Span_Pattern_Passes()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "abc".AsSpan(), "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null,
            pattern: new Regex(@"^[a-z]+$"),
            errors, invariant);
        Assert.False(hasErrors);
    }

    [Fact]
    public void Span_MultipleRules_AllReported()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "".AsSpan(), "x", 1, 0, null,
            notEmpty: true, minLength: 3, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
        // Both NotEmpty and MinLength fire.
        Assert.True(errors.Count >= 2);
    }

    [Fact]
    public void Span_RawValueLazilyMaterialized()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "abc".AsSpan(), "x", 5, 2, "ColumnName",
            notEmpty: false, minLength: 10, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
        Assert.Equal("abc", errors[0].RawValue);
        Assert.Equal(5, errors[0].RowNumber);
        Assert.Equal(2, errors[0].ColumnIndex);
        Assert.Equal("ColumnName", errors[0].ColumnName);
    }

    [Fact]
    public void Span_NullErrorList_StillReportsViaReturn()
    {
        var hasErrors = PropertyValidationRunner.Validate(
            "".AsSpan(), "x", 1, 0, null,
            notEmpty: true, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors: null, invariant);
        Assert.True(hasErrors);
    }

    // ───── String overload ─────

    [Fact]
    public void String_NotEmpty_FailsOnWhitespace()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "   ", "x", 1, 0, null,
            notEmpty: true, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
    }

    [Fact]
    public void String_MinLength_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "ab", "x", 1, 0, null,
            notEmpty: false, minLength: 5, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
    }

    [Fact]
    public void String_MaxLength_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "toolong", "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: 3,
            rangeMin: null, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
    }

    [Fact]
    public void String_RangeMin_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "5", "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: 10, rangeMax: null, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
    }

    [Fact]
    public void String_RangeMax_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "100", "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: 50, pattern: null,
            errors, invariant);
        Assert.True(hasErrors);
    }

    [Fact]
    public void String_Range_NonNumericValue_Skipped()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "hello", "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: 10, rangeMax: 100, pattern: null,
            errors, invariant);
        Assert.False(hasErrors);
    }

    [Fact]
    public void String_Pattern_Fails()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "abc123", "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null,
            pattern: new Regex(@"^[a-z]+$"),
            errors, invariant);
        Assert.True(hasErrors);
    }

    [Fact]
    public void String_Pattern_Passes()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "abc", "x", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null,
            pattern: new Regex(@"^[a-z]+$"),
            errors, invariant);
        Assert.False(hasErrors);
    }

    [Fact]
    public void String_NullErrorList_OnlyReturnsBool()
    {
        var hasErrors = PropertyValidationRunner.Validate(
            "", "x", 1, 0, null,
            notEmpty: true, minLength: null, maxLength: null,
            rangeMin: null, rangeMax: null, pattern: null,
            errors: null, invariant);
        Assert.True(hasErrors);
    }

    [Fact]
    public void Span_AllRulesPass_NoErrors()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "abc".AsSpan(), "x", 1, 0, null,
            notEmpty: true, minLength: 1, maxLength: 10,
            rangeMin: null, rangeMax: null,
            pattern: new Regex(@"^[a-z]+$"),
            errors, invariant);
        Assert.False(hasErrors);
        Assert.Empty(errors);
    }

    [Fact]
    public void String_AllRulesPass_NoErrors()
    {
        var errors = new List<ValidationError>();
        var hasErrors = PropertyValidationRunner.Validate(
            "5", "x", 1, 0, null,
            notEmpty: true, minLength: 1, maxLength: 5,
            rangeMin: 1, rangeMax: 10, pattern: null,
            errors, invariant);
        Assert.False(hasErrors);
    }
}
