using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using Xunit;

namespace HeroParser.Tests.Mapping;

[Trait("Category", "Unit")]
public class FixedWidthColumnBuilderTests
{
    [Fact]
    public void Start_SetsStartPosition()
    {
        var builder = new FixedWidthColumnBuilder();
        builder.Start(5);
        Assert.Equal(5, builder.StartPosition);
    }

    [Fact]
    public void Length_SetsFieldLength()
    {
        var builder = new FixedWidthColumnBuilder();
        builder.Length(10);
        Assert.Equal(10, builder.FieldLength);
    }

    [Fact]
    public void End_ComputesLengthFromStart()
    {
        var builder = new FixedWidthColumnBuilder();
        builder.Start(0).End(10);
        Assert.Equal(10, builder.ResolvedFieldLength);
    }

    [Fact]
    public void End_OverridesLength()
    {
        var builder = new FixedWidthColumnBuilder();
        builder.Start(0).Length(5).End(10);
        Assert.Equal(10, builder.ResolvedFieldLength);
    }

    [Fact]
    public void PadChar_SetsPadChar()
    {
        var builder = new FixedWidthColumnBuilder();
        builder.PadChar('0');
        Assert.Equal('0', builder.FieldPadChar);
    }

    [Fact]
    public void Alignment_SetsAlignment()
    {
        var builder = new FixedWidthColumnBuilder();
        builder.Alignment(FieldAlignment.Right);
        Assert.Equal(FieldAlignment.Right, builder.FieldAlignment);
    }

    [Fact]
    public void Format_SetsFormatString()
    {
        var builder = new FixedWidthColumnBuilder();
        builder.Format("yyyy-MM-dd");
        Assert.Equal("yyyy-MM-dd", builder.FormatString);
    }

    [Fact]
    public void Validation_BuildsCorrectly()
    {
        var builder = new FixedWidthColumnBuilder();
        builder.NotEmpty().MaxLength(10).MinLength(2).Range(0, 100).Pattern("^[A-Z]+$");
        var validation = builder.BuildValidation();
        Assert.NotNull(validation);
        Assert.True(validation.NotEmpty);
        Assert.Equal(10, validation.MaxLength);
        Assert.Equal(2, validation.MinLength);
        Assert.Equal(0, validation.RangeMin);
        Assert.Equal(100, validation.RangeMax);
        Assert.NotNull(validation.Pattern);
    }

    [Fact]
    public void BuildValidation_ReturnsNull_WhenNoRulesConfigured()
    {
        var builder = new FixedWidthColumnBuilder();
        builder.Start(0).Length(10);
        Assert.Null(builder.BuildValidation());
    }

    [Fact]
    public void FluentChaining_ReturnsThis()
    {
        var builder = new FixedWidthColumnBuilder();
        var result = builder.Start(0).Length(10).PadChar(' ').Alignment(FieldAlignment.Left).Format("F2").NotNull().NotEmpty();
        Assert.Same(builder, result);
    }
}
