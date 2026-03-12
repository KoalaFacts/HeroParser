using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using Xunit;

namespace HeroParser.Tests.Mapping;

[Trait("Category", "Unit")]
public class FixedWidthMapTests
{
    public class Record
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public decimal Amount { get; set; }
    }

    [Fact]
    public void Map_BuildsReadDescriptor_WithCorrectPositions()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10))
           .Map(r => r.Value, c => c.Start(10).Length(5))
           .Map(r => r.Amount, c => c.Start(15).Length(10));

        var descriptor = map.BuildReadDescriptor();

        Assert.Equal(3, descriptor.Properties.Length);
        Assert.Equal("Name", descriptor.Properties[0].Name);
        Assert.Equal(0, descriptor.Properties[0].Start);
        Assert.Equal(10, descriptor.Properties[0].Length);
        Assert.Equal("Value", descriptor.Properties[1].Name);
        Assert.Equal(10, descriptor.Properties[1].Start);
        Assert.Equal(5, descriptor.Properties[1].Length);
    }

    [Fact]
    public void Map_WithEnd_ComputesLength()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).End(10));

        var descriptor = map.BuildReadDescriptor();

        Assert.Equal(10, descriptor.Properties[0].Length);
    }

    [Fact]
    public void Map_WithPadCharAndAlignment_PassesThrough()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Value, c => c.Start(0).Length(5).PadChar('0').Alignment(FieldAlignment.Right));

        var descriptor = map.BuildReadDescriptor();

        Assert.Equal('0', descriptor.Properties[0].PadChar);
        Assert.Equal(FieldAlignment.Right, descriptor.Properties[0].Alignment);
    }

    [Fact]
    public void Map_WithValidation_PassesThrough()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10).NotEmpty().MaxLength(8));

        var descriptor = map.BuildReadDescriptor();

        Assert.NotNull(descriptor.Properties[0].Validation);
        Assert.True(descriptor.Properties[0].Validation!.NotEmpty);
        Assert.Equal(8, descriptor.Properties[0].Validation!.MaxLength);
    }

    [Fact]
    public void Map_BuildsWriteTemplates_WithCorrectFields()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10))
           .Map(r => r.Value, c => c.Start(10).Length(5).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(15).Length(10).Format("F2"));

        var templates = map.BuildWriteTemplates();

        Assert.Equal(3, templates.Length);
        Assert.Equal("Name", templates[0].MemberName);
        Assert.Equal(0, templates[0].Start);
        Assert.Equal(10, templates[0].Length);
        Assert.Equal("Value", templates[1].MemberName);
        Assert.Equal('0', templates[1].PadChar);
        Assert.Equal(FieldAlignment.Right, templates[1].Alignment);
        Assert.Equal("F2", templates[2].Format);
    }

    [Fact]
    public void Map_ThrowsOnDuplicateProperty()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(10));

        Assert.Throws<InvalidOperationException>(() =>
            map.Map(r => r.Name, c => c.Start(10).Length(10)));
    }

    [Fact]
    public void Map_ThrowsOnMissingStart()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Length(10));

        Assert.Throws<InvalidOperationException>(map.BuildReadDescriptor);
    }

    [Fact]
    public void Map_ThrowsOnMissingLength()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0));

        Assert.Throws<InvalidOperationException>(map.BuildReadDescriptor);
    }

    [Fact]
    public void Map_FluentChaining_ReturnsThis()
    {
        var map = new FixedWidthMap<Record>();
        var result = map
            .Map(r => r.Name, c => c.Start(0).Length(10))
            .Map(r => r.Value, c => c.Start(10).Length(5));

        Assert.Same(map, result);
    }

    public class RecordMap : FixedWidthMap<Record>
    {
        public RecordMap()
        {
            Map(r => r.Name, c => c.Start(0).Length(10));
            Map(r => r.Value, c => c.Start(10).Length(5));
            Map(r => r.Amount, c => c.Start(15).Length(10));
        }
    }

    [Fact]
    public void Map_Subclass_WorksLikeInline()
    {
        var map = new RecordMap();
        var descriptor = map.BuildReadDescriptor();

        Assert.Equal(3, descriptor.Properties.Length);
        Assert.Equal("Name", descriptor.Properties[0].Name);
    }
}
