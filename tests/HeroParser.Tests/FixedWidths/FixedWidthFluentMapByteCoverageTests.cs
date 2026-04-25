using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

/// <summary>
/// Drives the FixedWidth fluent-map descriptor binder (FixedWidthRecordBinder /
/// FixedWidthDescriptorBinder) through both char and byte (PipeReader) paths
/// with all primitive types. Targets the descriptor-binder coverage gaps
/// (~412 lines missing) without relying on source-generated binders.
/// </summary>
[SuppressMessage("Trimming", "IL2026", Justification = "Fluent maps require reflection.")]
[SuppressMessage("AOT", "IL3050", Justification = "Fluent maps require expression compilation.")]
[Trait("Category", "Unit")]
public class FixedWidthFluentMapByteCoverageTests
{
    public class FluentRecord
    {
        public string? Text { get; set; }
        public int IntValue { get; set; }
        public long LongValue { get; set; }
        public short ShortValue { get; set; }
        public decimal DecimalValue { get; set; }
        public double DoubleValue { get; set; }
        public bool BoolValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public Guid GuidValue { get; set; }
    }

    public class NullableFluentRecord
    {
        public int? IntValue { get; set; }
        public decimal? DecimalValue { get; set; }
        public bool? BoolValue { get; set; }
        public DateTime? DateTimeValue { get; set; }
    }

    private static FixedWidthMap<FluentRecord> BuildMap()
    {
        var map = new FixedWidthMap<FluentRecord>();
        map.Map(r => r.Text, c => c.Start(0).Length(10))
           .Map(r => r.IntValue, c => c.Start(10).Length(5).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.LongValue, c => c.Start(15).Length(10).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.ShortValue, c => c.Start(25).Length(5).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.DecimalValue, c => c.Start(30).Length(8))
           .Map(r => r.DoubleValue, c => c.Start(38).Length(8))
           .Map(r => r.BoolValue, c => c.Start(46).Length(5))
           .Map(r => r.DateTimeValue, c => c.Start(51).Length(10))
           .Map(r => r.GuidValue, c => c.Start(61).Length(36));
        return map;
    }

    private static string BuildSampleLine()
        => "hello     " +
           "00042" +
           "0000000007" +
           "00010" +
           "12345.67" +
           "3.141593" +
           "true " +
           "2024-01-15" +
           "12345678-1234-1234-1234-123456789012";

    [Fact]
    public void FluentMap_FromText_AllTypes()
    {
        var result = FixedWidth.Read<FluentRecord>().WithMap(BuildMap()).FromText(BuildSampleLine() + "\n");
        Assert.Single(result.Records);
        var rec = result.Records[0];
        Assert.Equal("hello", rec.Text);
        Assert.Equal(42, rec.IntValue);
        Assert.Equal(7L, rec.LongValue);
        Assert.Equal((short)10, rec.ShortValue);
        Assert.Equal(12345.67m, rec.DecimalValue);
        Assert.Equal(3.141593, rec.DoubleValue, 5);
        Assert.True(rec.BoolValue);
        Assert.Equal(new DateTime(2024, 1, 15), rec.DateTimeValue);
        Assert.NotEqual(Guid.Empty, rec.GuidValue);
    }

    [Fact]
    public void FluentMap_FromStream_AllTypes()
    {
        var bytes = Encoding.UTF8.GetBytes(BuildSampleLine() + "\n");
        using var ms = new MemoryStream(bytes);
        var result = FixedWidth.Read<FluentRecord>().WithMap(BuildMap()).FromStream(ms);
        Assert.Single(result.Records);
        Assert.Equal("hello", result.Records[0].Text);
        Assert.Equal(42, result.Records[0].IntValue);
    }

    [Fact]
    public async Task FluentMap_FromPipeReader_AllTypes()
    {
        var bytes = Encoding.UTF8.GetBytes(BuildSampleLine() + "\n");
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        var records = new List<FluentRecord>();
        await foreach (var r in FixedWidth.Read<FluentRecord>()
            .WithMap(BuildMap())
            .FromPipeReaderAsync(pipe, TestContext.Current.CancellationToken))
        {
            records.Add(r);
        }
        Assert.Single(records);
        Assert.Equal("hello", records[0].Text);
    }

    [Fact]
    public void FluentMap_NullableTypes_BlanksBecomeNull()
    {
        var map = new FixedWidthMap<NullableFluentRecord>();
        map.Map(r => r.IntValue, c => c.Start(0).Length(5))
           .Map(r => r.DecimalValue, c => c.Start(5).Length(8))
           .Map(r => r.BoolValue, c => c.Start(13).Length(5))
           .Map(r => r.DateTimeValue, c => c.Start(18).Length(10));

        var text = new string(' ', 28) + "\n";
        var result = FixedWidth.Read<NullableFluentRecord>().WithMap(map).FromText(text);
        Assert.Single(result.Records);
        Assert.Null(result.Records[0].IntValue);
        Assert.Null(result.Records[0].DecimalValue);
        Assert.Null(result.Records[0].BoolValue);
        Assert.Null(result.Records[0].DateTimeValue);
    }

    [Fact]
    public void FluentMap_NullableTypes_PopulatedValues()
    {
        var map = new FixedWidthMap<NullableFluentRecord>();
        map.Map(r => r.IntValue, c => c.Start(0).Length(5).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.DecimalValue, c => c.Start(5).Length(8))
           .Map(r => r.BoolValue, c => c.Start(13).Length(5))
           .Map(r => r.DateTimeValue, c => c.Start(18).Length(10));

        var text = "00042" + "0000.001" + "true " + "2024-01-15" + "\n";
        var result = FixedWidth.Read<NullableFluentRecord>().WithMap(map).FromText(text);
        Assert.Single(result.Records);
        Assert.Equal(42, result.Records[0].IntValue);
        Assert.Equal(0.001m, result.Records[0].DecimalValue);
        Assert.True(result.Records[0].BoolValue);
        Assert.Equal(new DateTime(2024, 1, 15), result.Records[0].DateTimeValue);
    }

    [Fact]
    public async Task FluentMap_FromStreamAsync_MultipleRows()
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= 3; i++)
        {
            sb.Append(BuildSampleLine()[..10]); // text
            sb.Append(i.ToString("D5"));        // int
            sb.Append(BuildSampleLine()[15..]); // remainder
            sb.Append('\n');
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        using var ms = new MemoryStream(bytes);

        var records = new List<FluentRecord>();
        await foreach (var r in FixedWidth.Read<FluentRecord>()
            .WithMap(BuildMap())
            .FromStreamAsync(ms, TestContext.Current.CancellationToken))
        {
            records.Add(r);
        }
        Assert.Equal(3, records.Count);
        Assert.Equal(1, records[0].IntValue);
        Assert.Equal(3, records[2].IntValue);
    }

    [Fact]
    public async Task FluentMap_FromFileAsync_RoundTrip()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempPath, BuildSampleLine() + "\n", TestContext.Current.CancellationToken);
            var records = new List<FluentRecord>();
            await foreach (var r in FixedWidth.Read<FluentRecord>()
                .WithMap(BuildMap())
                .FromFileAsync(tempPath, TestContext.Current.CancellationToken))
            {
                records.Add(r);
            }
            Assert.Single(records);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void FluentMap_WithCulture_AppliesToParsing()
    {
        var map = new FixedWidthMap<NullableFluentRecord>();
        map.Map(r => r.DecimalValue, c => c.Start(0).Length(8));
        var text = "1234,567\n"; // comma decimal separator (de-DE culture)

        var result = FixedWidth.Read<NullableFluentRecord>()
            .WithMap(map)
            .WithCulture(CultureInfo.GetCultureInfo("de-DE"))
            .FromText(text);

        Assert.Single(result.Records);
        Assert.Equal(1234.567m, result.Records[0].DecimalValue);
    }
}
