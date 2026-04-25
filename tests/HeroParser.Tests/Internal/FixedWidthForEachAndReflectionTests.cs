using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using HeroParser.FixedWidths.Records;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the ForEach API and the reflection-based binder fallback in
/// FixedWidthRecordBinder, which existing tests don't reach. Reflection
/// binding kicks in for record types that lack <c>[GenerateBinder]</c>;
/// ForEach has its own zero-allocation code path distinct from FromText.
/// </summary>
[SuppressMessage("Trimming", "IL2026", Justification = "Reflection-based binders.")]
[SuppressMessage("AOT", "IL3050", Justification = "Reflection-based binders.")]
[Trait("Category", "Unit")]
public class FixedWidthForEachAndReflectionTests
{
    [GenerateBinder]
    public sealed class GenRow
    {
        [PositionalMap(Start = 0, Length = 5)] public string Name { get; set; } = "";
        [PositionalMap(Start = 5, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Age { get; set; }
    }

    // No [GenerateBinder] -> forces reflection / descriptor path
    public sealed class ReflectionRow
    {
        [PositionalMap(Start = 0, Length = 5)] public string Name { get; set; } = "";
        [PositionalMap(Start = 5, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Age { get; set; }
    }

    private static string Sample(int n = 3)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++)
            sb.AppendLine($"name{i}{(i + 1):D5}");
        return sb.ToString();
    }

    [Fact]
    public void ForEachFromText_GeneratedBinder()
    {
        // ForEach reuses the same instance per row, so copy out the values we want to assert.
        var snapshots = new List<(string, int)>();
        FixedWidth.Read<GenRow>().ForEachFromText(Sample(), r => snapshots.Add((r.Name, r.Age)));
        Assert.Equal(3, snapshots.Count);
        Assert.Equal(("name0", 1), snapshots[0]);
        Assert.Equal(("name2", 3), snapshots[2]);
    }

    [Fact]
    public void ForEachFromFile_GeneratedBinder()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, Sample());
            int count = 0;
            FixedWidth.Read<GenRow>().ForEachFromFile(tmp, _ => count++);
            Assert.Equal(3, count);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ForEachFromText_ReflectionBinder()
    {
        var snapshots = new List<(string, int)>();
        FixedWidth.Read<ReflectionRow>().ForEachFromText(Sample(), r => snapshots.Add((r.Name, r.Age)));
        Assert.Equal(3, snapshots.Count);
        Assert.Equal(("name1", 2), snapshots[1]);
    }

    [Fact]
    public void FromText_ReflectionBinder()
    {
        var r = FixedWidth.Read<ReflectionRow>().FromText(Sample());
        Assert.Equal(3, r.Records.Count);
        Assert.Equal("name2", r.Records[2].Name);
        Assert.Equal(3, r.Records[2].Age);
    }

    [Fact]
    public void FromStream_ReflectionBinder()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(Sample()));
        var r = FixedWidth.Read<ReflectionRow>().FromStream(ms);
        Assert.Equal(3, r.Records.Count);
    }

    [Fact]
    public async Task FromStreamAsync_ReflectionBinder()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(Sample()));
        var rows = new List<ReflectionRow>();
        await foreach (var r in FixedWidth.Read<ReflectionRow>()
            .FromStreamAsync(ms, TestContext.Current.CancellationToken))
        {
            rows.Add(r);
        }
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void ReflectionBinder_WithCulture_PropagatesToParsing()
    {
        // Comma decimal separator under de-DE
        var r = FixedWidth.Read<DecimalRow>()
            .WithCulture(CultureInfo.GetCultureInfo("de-DE"))
            .FromText("01234,56\n");
        Assert.Single(r.Records);
        Assert.Equal(1234.56m, r.Records[0].Amount);
    }

    public sealed class DecimalRow
    {
        [PositionalMap(Start = 0, Length = 8)] public decimal Amount { get; set; }
    }

    [Fact]
    public void ReflectionBinder_WithNullValues_ProducesDefaults()
    {
        var r = FixedWidth.Read<NullableRow>()
            .WithNullValues("NULL")
            .FromText("aliceNULL \n");
        Assert.Single(r.Records);
        Assert.Null(r.Records[0].Age);
    }

    public sealed class NullableRow
    {
        [PositionalMap(Start = 0, Length = 5)] public string Name { get; set; } = "";
        [PositionalMap(Start = 5, Length = 5)] public int? Age { get; set; }
    }

    [Fact]
    public void ReflectionBinder_WithFluentMap_ChainedConfig()
    {
        var map = new FixedWidthMap<DecimalRow>();
        map.Map(r => r.Amount, c => c.Start(0).Length(8));
        var r = FixedWidth.Read<DecimalRow>().WithMap(map).FromText("01234,56\n");
        Assert.Single(r.Records);
    }

    [Fact]
    public void ReflectionBinder_RegisterCustomConverter()
    {
        var r = FixedWidth.Read<ReflectionRow>()
            .RegisterConverter<int>(MultiplyParseConverter)
            .FromText(Sample(2));
        Assert.Equal(2, r.Records.Count);
        // Custom converter doubles the parsed integer.
        Assert.Equal(2, r.Records[0].Age);
        Assert.Equal(4, r.Records[1].Age);
    }

    private static bool MultiplyParseConverter(ReadOnlySpan<char> s, CultureInfo c, string? f, out int result)
    {
        if (int.TryParse(s, NumberStyles.Any, c, out var parsed))
        {
            result = parsed * 2;
            return true;
        }
        result = 0;
        return false;
    }

    [Fact]
    public void ForEachFromText_WithoutHeader_AllowsAllRowsAsData()
    {
        // When a record type doesn't request a header row, the entire file is data.
        var rows = new List<ReflectionRow>();
        FixedWidth.Read<ReflectionRow>().WithoutHeader().ForEachFromText(Sample(2), rows.Add);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ForEachFromText_NullCallback_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FixedWidth.Read<ReflectionRow>().ForEachFromText(Sample(), null!));
    }

    [Fact]
    public void ForEach_WithFluentMap_NotSupported_Throws()
    {
        // The builder explicitly throws when ForEach is used together with a fluent map.
        var map = new FixedWidthMap<DecimalRow>();
        map.Map(r => r.Amount, c => c.Start(0).Length(8));
        Assert.Throws<NotSupportedException>(() =>
            FixedWidth.Read<DecimalRow>()
                .WithMap(map)
                .ForEachFromText("01234,56\n", _ => { }));
    }

    [Fact]
    public void Reflection_OnError_SkipParse_Continues()
    {
        // Row 2 is non-numeric; OnError handler skips it.
        var r = FixedWidth.Read<ReflectionRow>()
            .WithoutHeader()
            .OnError((_, _) => FixedWidthDeserializeErrorAction.SkipRecord)
            .FromText("name100001\nfoo  XXXXX\nname300003\n");
        // At least one record should bind (the well-formed ones).
        Assert.True(r.Records.Count >= 1);
    }
}
