using HeroParser.Cli;
using System.Text;
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
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("0")]
    [InlineData("+42")]
    [InlineData("-2147483649")]
    [InlineData("9223372036854775807")]
    [InlineData("9223372036854775808")]
    [InlineData("123.45")]
    [InlineData("1e3")]
    [InlineData("1e309")]
    [InlineData("1.7976931348623157E+308")]
    [InlineData(".5")]
    [InlineData(" 42 ")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("true")]
    [InlineData("2026-01-01")]
    [InlineData("11112222-3333-4444-5555-666677778888")]
    [InlineData("\"42\"")]
    [InlineData("北")]
    public void ObserveCellUtf8_MatchesStringObservation(string value)
    {
        var expected = new DynamicColumnStats();
        var actual = new DynamicColumnStats();

        DynamicProfiler.ObserveCell(expected, value);
        DynamicProfiler.ObserveCellUtf8(actual, Encoding.UTF8.GetBytes(value));

        AssertSameStats(expected, actual);
    }

    [Fact]
    public void ObserveCellUtf8_RepeatedAndHighCardinalityValuesMatchStringObservation()
    {
        var expected = new DynamicColumnStats();
        var actual = new DynamicColumnStats();
        string[] recurring = ["North", "north", "South", "true", "false", "2026-01-01", "北", " ", "", "123.45"];

        foreach (string value in Enumerable.Range(0, 300).Select(i => recurring[i % recurring.Length])
            .Concat(Enumerable.Range(0, 110).Select(i => $"unique{i}"))
            .Concat(Enumerable.Repeat("North", 30))
            .Append(new string('x', 300)))
        {
            DynamicProfiler.ObserveCell(expected, value);
            DynamicProfiler.ObserveCellUtf8(actual, Encoding.UTF8.GetBytes(value));
        }

        AssertSameStats(expected, actual);
    }

    [Fact]
    public void ObserveCellUtf8_SixteenAlternatingCategoriesMatchStringObservation()
    {
        var expected = new DynamicColumnStats();
        var actual = new DynamicColumnStats();
        string[] values = [.. Enumerable.Range(0, 16).Select(i => $"category{i}")];

        for (int i = 0; i < 320; i++)
        {
            string value = values[i % values.Length];
            DynamicProfiler.ObserveCell(expected, value);
            DynamicProfiler.ObserveCellUtf8(actual, Encoding.UTF8.GetBytes(value));
        }

        Assert.Equal(16, actual.ValueCounts.Count);
        AssertSameStats(expected, actual);
    }

    [Fact]
    public void ObserveCellUtf8_DisablesCacheAfterSeventeenByteDistinctVariants()
    {
        var expected = new DynamicColumnStats();
        var actual = new DynamicColumnStats();
        string[] variants = [.. Enumerable.Range(0, 17).Select(mask =>
            new string([.. "abcdefgh".Select((value, index) =>
                (mask & (1 << index)) == 0 ? value : char.ToUpperInvariant(value))]))];

        for (int i = 0; i < 340; i++)
        {
            string value = variants[i % variants.Length];
            DynamicProfiler.ObserveCell(expected, value);
            DynamicProfiler.ObserveCellUtf8(actual, Encoding.UTF8.GetBytes(value));
        }

        Assert.Single(actual.ValueCounts);
        Assert.NotNull(actual.Utf8Cache);
        Assert.True(actual.Utf8Cache.IsDisabled);
        AssertSameStats(expected, actual);
    }

    private static void AssertSameStats(DynamicColumnStats expected, DynamicColumnStats actual)
    {
        Assert.Equal(expected.NullCount, actual.NullCount);
        Assert.Equal(expected.NonNullCount, actual.NonNullCount);
        Assert.Equal(expected.IntCount, actual.IntCount);
        Assert.Equal(expected.LongCount, actual.LongCount);
        Assert.Equal(expected.DecimalCount, actual.DecimalCount);
        Assert.Equal(expected.BoolCount, actual.BoolCount);
        Assert.Equal(expected.TrueCount, actual.TrueCount);
        Assert.Equal(expected.FalseCount, actual.FalseCount);
        Assert.Equal(expected.DateTimeCount, actual.DateTimeCount);
        Assert.Equal(expected.GuidCount, actual.GuidCount);
        Assert.Equal(expected.StringCount, actual.StringCount);
        Assert.Equal(expected.Min, actual.Min);
        Assert.Equal(expected.Max, actual.Max);
        Assert.Equal(expected.Sum, actual.Sum);
        Assert.Equal(expected.ValueCounts, actual.ValueCounts);
        Assert.Equal(expected.CategoriesTruncated, actual.CategoriesTruncated);
    }

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
        Assert.True(stats[0].CategoriesTruncated);
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
    public void Analyze_DoesNotRetainOversizedCategoryValues()
    {
        var stats = DynamicProfiler.Analyze(["Col"], [[new string('x', 1000)]]);

        Assert.Empty(stats[0].ValueCounts);
        Assert.True(stats[0].CategoriesTruncated);
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
