using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Validation;

public class ValidationExceptionTests
{
    private static ValidationError MakeError(
        int row, int colIndex, string? colName, string prop, string rule, string message, string? raw) =>
        new()
        {
            RowNumber = row,
            ColumnIndex = colIndex,
            ColumnName = colName,
            PropertyName = prop,
            Rule = rule,
            Message = message,
            RawValue = raw
        };

    [Fact]
    [Trait("Category", "Unit")]
    public void SingleError_MessageContainsRowAndColumnAndPropertyAndRule()
    {
        var error = MakeError(2, 1, "Amount", "Amount", "NotNull", "Value is required", "");
        var ex = new ValidationException([error]);

        Assert.StartsWith("Validation failed:", ex.Message);
        Assert.Contains("Row 2", ex.Message);
        Assert.Contains("Column 'Amount' (index 1)", ex.Message);
        Assert.Contains("Property 'Amount'", ex.Message);
        Assert.Contains("[NotNull]", ex.Message);
        Assert.Contains("Value is required", ex.Message);
        Assert.Contains("(raw: '')", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SingleError_WithoutColumnName_ShowsColumnIndex()
    {
        var error = MakeError(5, 3, null, "Price", "Range", "Value must be between 0 and 100000", "-50");
        var ex = new ValidationException([error]);

        Assert.Contains("Column index 3", ex.Message);
        Assert.DoesNotContain("Column '", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SingleError_WithNullRawValue_OmitsRawSection()
    {
        var error = MakeError(2, 0, "Id", "TransactionId", "NotNull", "Value is required", null);
        var ex = new ValidationException([error]);

        Assert.DoesNotContain("(raw:", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MultipleErrors_MessageContainsCount()
    {
        var errors = new[]
        {
            MakeError(2, 0, "Id", "TransactionId", "NotNull", "Value is required", ""),
            MakeError(3, 1, "Amount", "Amount", "Range", "Value must be between 0 and 100000", "-10")
        };
        var ex = new ValidationException(errors);

        Assert.StartsWith("2 validation errors occurred:", ex.Message);
        Assert.Contains("Row 2", ex.Message);
        Assert.Contains("Row 3", ex.Message);
        Assert.Contains("[NotNull]", ex.Message);
        Assert.Contains("[Range]", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MultipleErrors_EachLineContainsFullContext()
    {
        var errors = new[]
        {
            MakeError(2, 0, "Id", "TransactionId", "NotNull", "Value is required", ""),
            MakeError(3, 2, "Currency", "Currency", "MinLength", "Value is shorter than minimum length of 3", "US")
        };
        var ex = new ValidationException(errors);

        // First error line
        Assert.Contains("Column 'Id' (index 0), Property 'TransactionId': [NotNull]", ex.Message);
        // Second error line
        Assert.Contains("Column 'Currency' (index 2), Property 'Currency': [MinLength]", ex.Message);
        Assert.Contains("(raw: 'US')", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MoreThanThreeErrors_TruncatesWithCount()
    {
        var errors = new[]
        {
            MakeError(2, 0, "Id", "Id", "NotNull", "Value is required", ""),
            MakeError(3, 1, "Amount", "Amount", "Range", "Out of range", "-1"),
            MakeError(4, 2, "Currency", "Currency", "MinLength", "Too short", "U"),
            MakeError(5, 3, "Reference", "Reference", "Pattern", "No match", "bad")
        };
        var ex = new ValidationException(errors);

        Assert.StartsWith("4 validation errors occurred:", ex.Message);
        // Only first 3 are shown inline
        Assert.Contains("Row 2", ex.Message);
        Assert.Contains("Row 3", ex.Message);
        Assert.Contains("Row 4", ex.Message);
        Assert.DoesNotContain("Row 5", ex.Message);
        Assert.Contains("... and 1 more.", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ErrorsProperty_ExposesAllErrors()
    {
        var errors = new[]
        {
            MakeError(2, 0, "Id", "Id", "NotNull", "Value is required", ""),
            MakeError(3, 1, "Amount", "Amount", "Range", "Out of range", "-1")
        };
        var ex = new ValidationException(errors);

        Assert.Equal(2, ex.Errors.Count);
        Assert.Equal("NotNull", ex.Errors[0].Rule);
        Assert.Equal("Range", ex.Errors[1].Rule);
    }
}
