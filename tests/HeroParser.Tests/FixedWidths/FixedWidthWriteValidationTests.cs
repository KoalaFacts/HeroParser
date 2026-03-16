using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

public class FixedWidthWriteValidationTests
{
    #region Test Models

    private class ValidatedRecord
    {
        [PositionalMap(Start = 0, Length = 10)]
        [Validate(NotNull = true)]
        public string? Name { get; set; }

        [PositionalMap(Start = 10, Length = 5)]
        [Validate(MinLength = 2, MaxLength = 4)]
        public string? Code { get; set; }
    }

    private class RangeRecord
    {
        [PositionalMap(Start = 0, Length = 5)]
        [Validate(RangeMin = 0, RangeMax = 100)]
        public int Score { get; set; }
    }

    private class NoValidationRecord
    {
        [PositionalMap(Start = 0, Length = 10)]
        public string? Name { get; set; }
    }

    #endregion

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_NotNull_NullValue_ThrowsValidationException()
    {
        var records = new[] { new ValidatedRecord { Name = null, Code = "AB" } };

        var ex = Assert.Throws<ValidationException>(() => FixedWidth.WriteToText(records));

        Assert.Single(ex.Errors);
        Assert.Equal("NotNull", ex.Errors[0].Rule);
        Assert.Equal("Name", ex.Errors[0].PropertyName);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_MaxLength_Exceeded_ThrowsValidationException()
    {
        var records = new[] { new ValidatedRecord { Name = "Alice", Code = "TOOLONG" } };

        var ex = Assert.Throws<ValidationException>(() => FixedWidth.WriteToText(records));

        Assert.Single(ex.Errors);
        Assert.Equal("MaxLength", ex.Errors[0].Rule);
        Assert.Equal("Code", ex.Errors[0].PropertyName);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_MinLength_TooShort_ThrowsValidationException()
    {
        var records = new[] { new ValidatedRecord { Name = "Alice", Code = "A" } };

        var ex = Assert.Throws<ValidationException>(() => FixedWidth.WriteToText(records));

        Assert.Single(ex.Errors);
        Assert.Equal("MinLength", ex.Errors[0].Rule);
        Assert.Equal("Code", ex.Errors[0].PropertyName);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_RangeMin_Violated_ThrowsValidationException()
    {
        var records = new[] { new RangeRecord { Score = -1 } };

        var ex = Assert.Throws<ValidationException>(() => FixedWidth.WriteToText(records));

        Assert.Single(ex.Errors);
        Assert.Equal("Range", ex.Errors[0].Rule);
        Assert.Equal("Score", ex.Errors[0].PropertyName);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_RangeMax_Violated_ThrowsValidationException()
    {
        var records = new[] { new RangeRecord { Score = 101 } };

        var ex = Assert.Throws<ValidationException>(() => FixedWidth.WriteToText(records));

        Assert.Single(ex.Errors);
        Assert.Equal("Range", ex.Errors[0].Rule);
        Assert.Equal("Score", ex.Errors[0].PropertyName);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_ValidationPasses_WritesRecord()
    {
        var records = new[] { new ValidatedRecord { Name = "Alice", Code = "AB" } };

        var result = FixedWidth.WriteToText(records);

        Assert.Contains("Alice", result);
        Assert.Contains("AB", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_LenientMode_SkipsValidation_WritesRecord()
    {
        var records = new[] { new ValidatedRecord { Name = null, Code = "AB" } };
        var options = new FixedWidthWriteOptions { ValidationMode = ValidationMode.Lenient };

        // Should not throw in Lenient mode
        var result = FixedWidth.WriteToText(records, options);

        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_NoValidationAttributes_WritesRecord()
    {
        var records = new[] { new NoValidationRecord { Name = null } };

        // No exception even with null — no [Validate] attribute
        var result = FixedWidth.WriteToText(records);

        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_SecondRecord_Invalid_ThrowsOnSecondRow()
    {
        var records = new[]
        {
            new ValidatedRecord { Name = "Alice", Code = "AB" },
            new ValidatedRecord { Name = null, Code = "AB" }
        };

        var ex = Assert.Throws<ValidationException>(() => FixedWidth.WriteToText(records));

        Assert.Single(ex.Errors);
        Assert.Equal("NotNull", ex.Errors[0].Rule);
        Assert.Equal(2, ex.Errors[0].RowNumber);
    }
}
