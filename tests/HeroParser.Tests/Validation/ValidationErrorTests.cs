using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Validation;

public class ValidationErrorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ValidationError_CanBeCreatedWithAllProperties()
    {
        var error = new ValidationError
        {
            RowNumber = 5,
            ColumnIndex = 2,
            ColumnName = "Amount",
            PropertyName = "Amount",
            Rule = "NotNull",
            Message = "Value is required",
            RawValue = ""
        };
        Assert.Equal(5, error.RowNumber);
        Assert.Equal(2, error.ColumnIndex);
        Assert.Equal("Amount", error.ColumnName);
        Assert.Equal("Amount", error.PropertyName);
        Assert.Equal("NotNull", error.Rule);
        Assert.Equal("Value is required", error.Message);
        Assert.Equal("", error.RawValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidationError_ColumnNameCanBeNull()
    {
        var error = new ValidationError
        {
            ColumnName = null,
            PropertyName = "Id",
            Rule = "NotNull",
            Message = "fail"
        };
        Assert.Null(error.ColumnName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidationError_RawValueCanBeNull()
    {
        var error = new ValidationError
        {
            PropertyName = "Id",
            Rule = "NotNull",
            Message = "fail",
            RawValue = null
        };
        Assert.Null(error.RawValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToString_WithColumnName_IncludesAllContext()
    {
        var error = new ValidationError
        {
            RowNumber = 5,
            ColumnIndex = 2,
            ColumnName = "Amount",
            PropertyName = "Amount",
            Rule = "Range",
            Message = "Value must be between 0 and 100000",
            RawValue = "-1.00"
        };

        var result = error.ToString();
        Assert.Equal("Row 5, Column 'Amount' (index 2), Property 'Amount': [Range] Value must be between 0 and 100000 (raw: '-1.00')", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToString_WithoutColumnName_UsesColumnIndex()
    {
        var error = new ValidationError
        {
            RowNumber = 3,
            ColumnIndex = 1,
            ColumnName = null,
            PropertyName = "Price",
            Rule = "NotNull",
            Message = "Value is required",
            RawValue = ""
        };

        var result = error.ToString();
        Assert.Equal("Row 3, Column index 1, Property 'Price': [NotNull] Value is required (raw: '')", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToString_WithoutRawValue_OmitsRawSection()
    {
        var error = new ValidationError
        {
            RowNumber = 2,
            ColumnIndex = 0,
            ColumnName = "Id",
            PropertyName = "TransactionId",
            Rule = "NotNull",
            Message = "Value is required",
            RawValue = null
        };

        var result = error.ToString();
        Assert.Equal("Row 2, Column 'Id' (index 0), Property 'TransactionId': [NotNull] Value is required", result);
    }
}
