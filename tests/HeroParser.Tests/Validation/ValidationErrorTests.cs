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
            Rule = "Required",
            Message = "Value is required",
            RawValue = ""
        };
        Assert.Equal(5, error.RowNumber);
        Assert.Equal(2, error.ColumnIndex);
        Assert.Equal("Amount", error.ColumnName);
        Assert.Equal("Amount", error.PropertyName);
        Assert.Equal("Required", error.Rule);
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
            Rule = "Required",
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
            Rule = "Required",
            Message = "fail",
            RawValue = null
        };
        Assert.Null(error.RawValue);
    }
}
