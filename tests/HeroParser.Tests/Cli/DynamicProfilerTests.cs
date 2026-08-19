using HeroParser.Cli;
using Xunit;

namespace HeroParser.Tests.Cli;

/// <summary>
/// Covers the dataset profiler that builds the context card sent to the model.
///
/// Everything the AI commands infer about a file comes from this card, so a wrong type
/// verdict or a mis-scaled percentage silently degrades every AI answer without failing
/// anything. The profiler is pure, so each verdict can be pinned directly.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class DynamicProfilerTests
{
    private static string TypeOfColumn(params string[] values)
    {
        var stats = DynamicProfiler.Analyze(["Col"], [.. values.Select(v => new[] { v })]);
        return DynamicProfiler.InferTypeName(stats[0]);
    }

    [Theory]
    [InlineData("Integer", "1", "2", "3")]
    [InlineData("Integer", "9223372036854775807", "1")]
    [InlineData("Decimal", "1.5", "2.25")]
    [InlineData("Decimal", "1", "2.5")]
    [InlineData("Boolean", "true", "false", "True")]
    [InlineData("Guid", "11112222-3333-4444-5555-666677778888")]
    [InlineData("DateTime", "2024-01-31", "2024-02-01")]
    [InlineData("String", "alpha", "beta")]
    public void InferTypeName_ClassifiesAColumnByWhatItHolds(string expected, params string[] values)
        => Assert.Equal(expected, TypeOfColumn(values));

    [Fact]
    public void InferTypeName_OneStringValue_DemotesTheWholeColumn()
    {
        // A single unparsable value means the column cannot be bound as a number, so the
        // profiler must not advertise it as one.
        Assert.Equal("String", TypeOfColumn("1", "2", "n/a"));
    }

    [Fact]
    public void InferTypeName_AllBlank_IsString()
        => Assert.Equal("String", TypeOfColumn("", "  "));

    [Fact]
    public void InferTypeName_MixedGuidAndDate_FallsBackToString()
        => Assert.Equal("String", TypeOfColumn("11112222-3333-4444-5555-666677778888", "2024-01-31"));

    [Fact]
    public void Analyze_CountsNullsAndValues()
    {
        var stats = DynamicProfiler.Analyze(["A", "B"], [["1", "x"], ["", "y"], ["3", "x"]]);

        Assert.Equal(2, stats[0].NonNullCount);
        Assert.Equal(1, stats[0].NullCount);
        Assert.Equal(1, stats[0].Min);
        Assert.Equal(3, stats[0].Max);
        Assert.Equal(4, stats[0].Sum);
        Assert.Equal(2, stats[1].ValueCounts["x"]);
    }

    [Fact]
    public void Analyze_ShortRow_CountsTheMissingColumnsAsNull()
    {
        // A ragged row must not throw or shift values into the wrong column.
        var stats = DynamicProfiler.Analyze(["A", "B", "C"], [["1"]]);

        Assert.Equal(1, stats[0].NonNullCount);
        Assert.Equal(1, stats[1].NullCount);
        Assert.Equal(1, stats[2].NullCount);
    }

    [Fact]
    public void Analyze_BooleanColumn_SplitsTrueAndFalse()
    {
        var stats = DynamicProfiler.Analyze(["Flag"], [["true"], ["false"], ["true"]]);
        Assert.Equal(2, stats[0].TrueCount);
        Assert.Equal(1, stats[0].FalseCount);
    }

    [Fact]
    public void Analyze_CategoryTrackingStopsAtOneHundredDistinctValues()
    {
        // Unbounded tracking would let a high-cardinality column exhaust memory.
        var rows = Enumerable.Range(0, 250).Select(i => new[] { $"v{i}" }).ToList();
        var stats = DynamicProfiler.Analyze(["Col"], rows);

        Assert.Equal(100, stats[0].ValueCounts.Count);
        Assert.Equal(250, stats[0].NonNullCount);
    }

    [Fact]
    public void Analyze_RepeatedValueBeyondTheCap_StillCounts()
    {
        var rows = Enumerable.Range(0, 150).Select(i => new[] { $"v{i}" }).ToList();
        rows.Add(["v0"]);
        var stats = DynamicProfiler.Analyze(["Col"], rows);

        Assert.Equal(2, stats[0].ValueCounts["v0"]);
    }

    [Fact]
    public void GenerateContextCard_DescribesNumericColumns()
    {
        string card = DynamicProfiler.GenerateContextCard("sales.csv", ["Amount"], [["10"], ["20"], ["30"]]);

        Assert.Contains("sales.csv", card, StringComparison.Ordinal);
        Assert.Contains("3 rows", card, StringComparison.Ordinal);
        Assert.Contains("**Amount** (Integer", card, StringComparison.Ordinal);
        Assert.Contains("Avg: 20.00", card, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateContextCard_DescribesBooleanColumns()
    {
        string card = DynamicProfiler.GenerateContextCard("f.csv", ["Flag"], [["true"], ["false"]]);
        Assert.Contains("Boolean. True: 1 (50.0%), False: 1 (50.0%)", card, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateContextCard_ListsTheTopCategories()
    {
        string card = DynamicProfiler.GenerateContextCard(
            "f.csv", ["City"], [["Oslo"], ["Oslo"], ["Bergen"], ["Tromso"], ["Alta"]]);

        Assert.Contains("4 distinct categories", card, StringComparison.Ordinal);
        Assert.Contains("Top values:", card, StringComparison.Ordinal);
        Assert.Contains("\"Oslo\" (40.0%)", card, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateContextCard_ReportsNullShare()
    {
        string card = DynamicProfiler.GenerateContextCard("f.csv", ["A"], [["1"], [""], ["3"], ["4"]]);
        Assert.Contains("25.0% Null", card, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateContextCard_EmptyDataset_SaysSo()
    {
        string card = DynamicProfiler.GenerateContextCard("empty.csv", ["A", "B"], []);

        Assert.Contains("0 rows", card, StringComparison.Ordinal);
        Assert.Contains("No data available.", card, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateContextCard_NumericColumnWithNoParsedValues_ReportsZeroRange()
    {
        // Min/Max start at their sentinel extremes; without values the card must not print those.
        string card = DynamicProfiler.GenerateContextCard("f.csv", ["A"], [["x"]]);
        Assert.DoesNotContain("E+308", card, StringComparison.Ordinal);
    }
}
