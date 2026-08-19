using System.Text;
using HeroParser.SeparatedValues.Validation;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers the validator's answers for input it cannot parse: empty files, whitespace-only
/// files, and data with no discernible delimiter.
///
/// Validation exists to give a caller a verdict instead of an exception, so these paths —
/// where there is nothing to validate — are the ones that decide whether a user gets a
/// clear reason or a stack trace. None of them had run.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class CsvValidatorEdgeCaseTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\r\n")]
    [InlineData(" \t \n ")]
    public void EmptyOrWhitespaceInput_IsReportedAsAnEmptyFile(string data)
    {
        var result = Csv.Validate(data);

        Assert.False(result.IsValid);
        Assert.Equal(CsvValidationErrorType.EmptyFile, Assert.Single(result.Errors).ErrorType);
        Assert.Equal(0, result.TotalRows);
        Assert.Equal(0, result.ColumnCount);
    }

    [Fact]
    public void EmptyInput_IsAcceptedWhenTheCallerAllowsIt()
    {
        var result = Csv.Validate(string.Empty, new CsvValidationOptions { AllowEmptyFile = true });

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(0, result.TotalRows);
    }

    [Fact]
    public void EmptyInput_ReportsTheConfiguredDelimiter()
    {
        var result = Csv.Validate(string.Empty, new CsvValidationOptions { Delimiter = ';', AllowEmptyFile = true });
        Assert.Equal(';', result.Delimiter);
    }

    [Fact]
    public void UndelimitedInput_ReportsThatDetectionFailed()
    {
        // Nothing here looks like a delimiter, so the validator has to say so rather than
        // pick one arbitrarily and report a wall of column-count errors.
        var result = Csv.Validate("one two three\nfour five\nsix\n");

        Assert.False(result.IsValid);
        Assert.Equal(CsvValidationErrorType.DelimiterDetectionFailed, Assert.Single(result.Errors).ErrorType);
        Assert.Contains("Could not detect delimiter", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleColumnExpectation_SkipsDetectionEntirely()
    {
        // With one expected column there is nothing to detect, so undelimited data is fine.
        var result = Csv.Validate("alpha\nbeta\ngamma\n", new CsvValidationOptions { ExpectedColumnCount = 1 });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Utf8Input_ValidatesLikeItsTextEquivalent()
    {
        const string csv = "a,b\n1,2\n";
        var fromText = Csv.Validate(csv);
        var fromBytes = Csv.Validate(Encoding.UTF8.GetBytes(csv).AsSpan());

        Assert.Equal(fromText.IsValid, fromBytes.IsValid);
        Assert.Equal(fromText.TotalRows, fromBytes.TotalRows);
        Assert.Equal(fromText.ColumnCount, fromBytes.ColumnCount);
    }

    [Fact]
    public void EmptyUtf8Input_IsReportedAsAnEmptyFile()
    {
        var result = Csv.Validate(ReadOnlySpan<byte>.Empty);
        Assert.Equal(CsvValidationErrorType.EmptyFile, Assert.Single(result.Errors).ErrorType);
    }
}
