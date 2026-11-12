using System.Globalization;
using HeroParser.AI;
using HeroParser.Htbs;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Validation;

/// <summary>
/// Validation messages are fixed English text, so the numbers in them use the invariant culture
/// whatever the current culture (or the reader's/writer's parse culture) is: under de-DE a range
/// message must read "1234.5 exceeds maximum 100.5", not "1234,5 exceeds maximum 100,5".
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class ValidationMessageCultureTests
{
    private const string EXPECTED_MAX = "Field 'Amount' value 1234.5 exceeds maximum 100.5.";
    private const string EXPECTED_MIN = "Field 'Amount' value -0.25 is less than minimum 0.5.";

    private static void WithCulture(string cultureName, Action action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void PropertyValidationRunner_Span_RangeMessages_UseInvariantNumbers(string cultureName)
    {
        WithCulture(cultureName, () =>
        {
            var errors = new List<ValidationError>();
            PropertyValidationRunner.Validate(
                "1234.5".AsSpan(), "Amount", 1, 0, null,
                notEmpty: false, minLength: null, maxLength: null,
                rangeMin: 0.5, rangeMax: 100.5, pattern: null,
                errors, CultureInfo.InvariantCulture);
            PropertyValidationRunner.Validate(
                "-0.25".AsSpan(), "Amount", 2, 0, null,
                notEmpty: false, minLength: null, maxLength: null,
                rangeMin: 0.5, rangeMax: 100.5, pattern: null,
                errors, CultureInfo.InvariantCulture);

            Assert.Equal([EXPECTED_MAX, EXPECTED_MIN], errors.Select(e => e.Message));
        });
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void PropertyValidationRunner_String_RangeMessages_UseInvariantNumbers(string cultureName)
    {
        WithCulture(cultureName, () =>
        {
            var errors = new List<ValidationError>();
            PropertyValidationRunner.Validate(
                "1234.5", "Amount", 1, 0, null,
                notEmpty: false, minLength: null, maxLength: null,
                rangeMin: 0.5, rangeMax: 100.5, pattern: null,
                errors, CultureInfo.InvariantCulture);
            PropertyValidationRunner.Validate(
                "-0.25", "Amount", 2, 0, null,
                notEmpty: false, minLength: null, maxLength: null,
                rangeMin: 0.5, rangeMax: 100.5, pattern: null,
                errors, CultureInfo.InvariantCulture);

            Assert.Equal([EXPECTED_MAX, EXPECTED_MIN], errors.Select(e => e.Message));
        });
    }

    [Fact]
    public void PropertyValidationRunner_ParseCultureDoesNotChangeMessageNumbers()
    {
        // A de-DE reader parses "1234,5" as 1234.5; the message still uses '.' (RawValue keeps the input).
        var errors = new List<ValidationError>();
        PropertyValidationRunner.Validate(
            "1234,5", "Amount", 1, 0, null,
            notEmpty: false, minLength: null, maxLength: null,
            rangeMin: 0.5, rangeMax: 100.5, pattern: null,
            errors, CultureInfo.GetCultureInfo("de-DE"));

        var error = Assert.Single(errors);
        Assert.Equal(EXPECTED_MAX, error.Message);
        Assert.Equal("1234,5", error.RawValue);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void WriteValidationRunner_RangeMessages_UseInvariantNumbers(string cultureName)
    {
        WithCulture(cultureName, () =>
        {
            var rules = new WritePropertyValidation(
                NotNull: false, NotEmpty: false, MaxLength: null, MinLength: null,
                RangeMin: 0.5, RangeMax: 100.5, Pattern: null);
            var errors = new List<ValidationError>();
            WriteValidationRunner.Validate(1234.5, "Amount", 1, 0, rules, errors, CultureInfo.InvariantCulture);
            WriteValidationRunner.Validate(-0.25m, "Amount", 2, 0, rules, errors, CultureInfo.InvariantCulture);

            Assert.Equal([EXPECTED_MAX, EXPECTED_MIN], errors.Select(e => e.Message));
        });
    }

    public class HtbAmountRecord
    {
        public int Id { get; set; }

        [Validate(RangeMin = 0.5, RangeMax = 100.5)]
        public double Amount { get; set; }
    }

    [Theory]
    [InlineData("de-DE", 1234.5, "value 1234.5 cannot exceed 100.5.")]
    [InlineData("de-DE", -0.25, "value -0.25 must be at least 0.5.")]
    [InlineData("fr-FR", 1234.5, "value 1234.5 cannot exceed 100.5.")]
    [InlineData("en-US", -0.25, "value -0.25 must be at least 0.5.")]
    public void HtbStrictRead_RangeMessages_UseInvariantNumbers(string cultureName, double amount, string expectedTail)
    {
        WithCulture(cultureName, () =>
        {
            using var ms = new MemoryStream();
            HeroParser.Htb.Write<HtbAmountRecord>().ToStream(ms, [new HtbAmountRecord { Id = 1, Amount = amount }], leaveOpen: true);
            ms.Position = 0;

            var ex = Assert.Throws<HtbException>(() =>
                HeroParser.Htb.Read<HtbAmountRecord>()
                    .WithValidationMode(ValidationMode.Strict)
                    .FromStream(ms, leaveOpen: true)
                    .ToList());
            Assert.EndsWith(expectedTail, ex.Message);
        });
    }

    private sealed class ToolCallRecord
    {
        [Validate(RangeMin = 0.5, RangeMax = 100.5)]
        public double Amount { get; set; }
    }

    [Theory]
    [InlineData("de-DE", "1234.5", "Property 'Amount' value 1234.5 cannot exceed 100.5.")]
    [InlineData("de-DE", "-0.25", "Property 'Amount' value -0.25 must be at least 0.5.")]
    [InlineData("fr-FR", "1234.5", "Property 'Amount' value 1234.5 cannot exceed 100.5.")]
    [InlineData("en-US", "-0.25", "Property 'Amount' value -0.25 must be at least 0.5.")]
    public void MapFromToolCall_RangeMessages_UseInvariantNumbers(string cultureName, string amount, string expected)
    {
        WithCulture(cultureName, () =>
        {
            var args = new Dictionary<string, object?> { { "Amount", amount } };

            var ex = Assert.Throws<LlmToolCallValidationException>(() => SchemaMetadata.MapFromToolCall<ToolCallRecord>(args));
            Assert.Contains(expected, ex.Message);
        });
    }
}
