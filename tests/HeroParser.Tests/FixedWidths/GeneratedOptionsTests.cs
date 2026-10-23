using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class GeneratedOptionsTests
{
    [Fact]
    public void GeneratedRecord_OnError_SkipsInvalidRowAndInvokesCallback()
    {
        int calls = 0;
        var rows = FixedWidth.Read<GeneratedOptionsRecord>()
            .OnError((_, _) => { calls++; return FixedWidthDeserializeErrorAction.SkipRecord; })
            .FromText("001Alice\nBADBob  ").ToList();
        Assert.Single(rows);
        Assert.Equal(1, rows[0].Id);
        Assert.Equal("Alice", rows[0].Name);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GeneratedRecord_CustomConverter_IsInvokedAndChangesValue()
    {
        int calls = 0;
        var rows = FixedWidth.Read<GeneratedOptionsRecord>()
            .RegisterConverter<int>((value, culture, format, out result) =>
            {
                calls++;
                result = int.Parse(value, culture) + 100;
                return true;
            })
            .FromText("001Alice").ToList();
        Assert.Single(rows);
        Assert.Equal(101, rows[0].Id);
        Assert.Equal("Alice", rows[0].Name);
        Assert.Equal(1, calls);
    }
}

[GenerateBinder]
public partial class GeneratedOptionsRecord
{
    [PositionalMap(Start = 0, Length = 3)]
    public int Id { get; set; }
    [PositionalMap(Start = 3, Length = 5)]
    public string Name { get; set; } = "";
}
