using HeroParser.SeparatedValues.Mapping;
using Xunit;

namespace HeroParser.Tests.Mapping;

[Trait("Category", "Unit")]
public class CsvColumnBuilderTests
{
    [Fact]
    public void Name_SetsHeaderName()
    {
        var builder = new CsvColumnBuilder();
        builder.Name("Ticker");
        Assert.Equal("Ticker", builder.HeaderName);
    }

    [Fact]
    public void Index_SetsColumnIndex()
    {
        var builder = new CsvColumnBuilder();
        builder.Index(3);
        Assert.Equal(3, builder.ColumnIndex);
    }

    [Fact]
    public void Format_SetsFormatString()
    {
        var builder = new CsvColumnBuilder();
        builder.Format("yyyy-MM-dd");
        Assert.Equal("yyyy-MM-dd", builder.FormatString);
    }

    [Fact]
    public void Required_SetsFlag()
    {
        var builder = new CsvColumnBuilder();
        builder.Required();
        Assert.True(builder.IsRequired);
    }

    [Fact]
    public void Validation_BuildsCorrectly()
    {
        var builder = new CsvColumnBuilder();
        builder.NotEmpty().MaxLength(50).MinLength(1).Range(0, 100).Pattern(@"^\d+$", 500);
        var validation = builder.BuildValidation();
        Assert.NotNull(validation);
        Assert.True(validation.NotEmpty);
        Assert.Equal(50, validation.MaxLength);
        Assert.Equal(1, validation.MinLength);
        Assert.Equal(0, validation.RangeMin);
        Assert.Equal(100, validation.RangeMax);
        Assert.NotNull(validation.Pattern);
    }

    [Fact]
    public void BuildValidation_ReturnsNull_WhenNoRulesConfigured()
    {
        var builder = new CsvColumnBuilder();
        builder.Name("Col");
        Assert.Null(builder.BuildValidation());
    }

    [Fact]
    public void FluentChaining_ReturnsThis()
    {
        var builder = new CsvColumnBuilder();
        var result = builder.Name("A").Index(0).Format("F2").Required().NotEmpty()
            .MaxLength(10).MinLength(1).Range(0, 99).Pattern(@"\w+");
        Assert.Same(builder, result);
    }
}
