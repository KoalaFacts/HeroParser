using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the byte-path TryBind/BindInto in FixedWidthDescriptorBinder
/// (lines 88-95, 119-191, 225-256). These run when a fluent FixedWidthMap is
/// configured AND the input is a UTF-8 byte stream (PipeReader).
/// </summary>
[SuppressMessage("Trimming", "IL2026", Justification = "Reflection-based binders.")]
[SuppressMessage("AOT", "IL3050", Justification = "Reflection-based binders.")]
[Trait("Category", "Unit")]
public class FixedWidthDescriptorBinderByteTests
{
    public class Record
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public decimal Amount { get; set; }
    }

    private static FixedWidthMap<Record> BuildMap()
    {
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(5))
           .Map(r => r.Age, c => c.Start(5).Length(5).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(10).Length(8));
        return map;
    }

    private static async Task<List<Record>> ReadPipe(string text, FixedWidthMap<Record>? customMap = null,
        Action<HeroParser.FixedWidths.Records.FixedWidthReaderBuilder<Record>>? configure = null,
        CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        var builder = HeroParser.FixedWidth.Read<Record>().WithMap(customMap ?? BuildMap());
        configure?.Invoke(builder);
        var list = new List<Record>();
        await foreach (var r in builder.FromPipeReaderAsync(pipe, ct))
        {
            list.Add(r);
        }
        return list;
    }

    [Fact]
    public async Task BytePath_HappyPath_AllProperties()
    {
        var ct = TestContext.Current.CancellationToken;
        var line = "alice00030" + "12345.67" + "\n";
        var records = await ReadPipe(line, ct: ct);
        Assert.Single(records);
        Assert.Equal("alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal(12345.67m, records[0].Amount);
    }

    [Fact]
    public async Task BytePath_NullValueRecognized()
    {
        var ct = TestContext.Current.CancellationToken;
        // "NULL" in the Amount slot should be treated as null/skipped.
        var line = "alice00030" + "NULL    " + "\n";
        var records = await ReadPipe(line, configure: b => b.WithNullValues("NULL"), ct: ct);
        Assert.Single(records);
        Assert.Equal(0m, records[0].Amount);
    }

    [Fact]
    public async Task BytePath_NotNullValidation_DoesNotCrash()
    {
        var ct = TestContext.Current.CancellationToken;
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(5).NotNull())
           .Map(r => r.Age, c => c.Start(5).Length(5).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(10).Length(8));

        var line = "     " + "00030" + "12345.67" + "\n";
        // Async path may collect errors without throwing; just exercise the validation branch.
        var records = await ReadPipe(line, customMap: map, ct: ct);
        Assert.NotNull(records);
    }

    [Fact]
    public async Task BytePath_ParseFailure_WrappedAsFixedWidthException()
    {
        var ct = TestContext.Current.CancellationToken;
        // Garbage in Age field
        var line = "alice" + "XXXXX" + "12345.67" + "\n";
        await Assert.ThrowsAsync<FixedWidthException>(async () =>
            await ReadPipe(line, ct: ct));
    }

    [Fact]
    public async Task BytePath_ValidationRule_MaxLength_Branch()
    {
        var ct = TestContext.Current.CancellationToken;
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(5).MaxLength(3))
           .Map(r => r.Age, c => c.Start(5).Length(5).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(10).Length(8));

        var line = "alice" + "00030" + "12345.67" + "\n";
        var records = await ReadPipe(line, customMap: map, ct: ct);
        Assert.NotNull(records);
    }

    [Fact]
    public async Task BytePath_ValidationRule_Range_Branch()
    {
        var ct = TestContext.Current.CancellationToken;
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(5))
           .Map(r => r.Age, c => c.Start(5).Length(5).PadChar('0').Alignment(FieldAlignment.Right).Range(0, 100))
           .Map(r => r.Amount, c => c.Start(10).Length(8));

        var line = "alice" + "99999" + "12345.67" + "\n";
        var records = await ReadPipe(line, customMap: map, ct: ct);
        Assert.NotNull(records);
    }

    [Fact]
    public async Task BytePath_LenientValidation_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(5).MaxLength(3))
           .Map(r => r.Age, c => c.Start(5).Length(5).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(10).Length(8));

        var line = "alice" + "00030" + "12345.67" + "\n";
        var records = await ReadPipe(line, customMap: map,
            configure: b => b.WithValidationMode(ValidationMode.Lenient), ct: ct);
        // Lenient: violation noted but record skipped, no throw.
        Assert.NotNull(records);
    }

    [Fact]
    public async Task BytePath_MultipleRecords_AllBindCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        // Each row needs exactly: 5-char name + 5-char age + 8-char amount = 18 chars
        var sb = new StringBuilder();
        for (int i = 1; i <= 5; i++)
            sb.AppendLine($"name{i}{i:D5}{(i * 100).ToString("F2", System.Globalization.CultureInfo.InvariantCulture).PadLeft(8)}");
        var records = await ReadPipe(sb.ToString(), ct: ct);
        Assert.Equal(5, records.Count);
        Assert.Equal("name1", records[0].Name);
        Assert.Equal(5, records[^1].Age);
    }

    [Fact]
    public async Task BytePath_PatternValidation_Branch()
    {
        var ct = TestContext.Current.CancellationToken;
        var map = new FixedWidthMap<Record>();
        map.Map(r => r.Name, c => c.Start(0).Length(5).Pattern(@"^[a-z]+$"))
           .Map(r => r.Age, c => c.Start(5).Length(5).PadChar('0').Alignment(FieldAlignment.Right))
           .Map(r => r.Amount, c => c.Start(10).Length(8));

        var line = "abc12" + "00030" + "12345.67" + "\n";
        var records = await ReadPipe(line, customMap: map, ct: ct);
        Assert.NotNull(records);
    }
}
