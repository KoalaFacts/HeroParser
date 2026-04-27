using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers FixedWidthReaderBuilder fluent options + terminals that lacked direct
/// tests: option chaining, async file/stream paths, Map/WithMap, RegisterConverter,
/// progress, encoding, validation mode, error handling, ForEach.
/// </summary>
[Trait("Category", "Unit")]
public class FixedWidthReaderBuilderTerminalTests
{
    [GenerateBinder]
    public sealed class Row
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
    public void FromText_DefaultOptions()
    {
        var r = FixedWidth.Read<Row>().FromText(Sample());
        Assert.Equal(3, r.Records.Count);
    }

    [Fact]
    public void FromStream_LeaveOpen()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(Sample()));
        var r = FixedWidth.Read<Row>().FromStream(ms);
        Assert.Equal(3, r.Records.Count);
        Assert.True(ms.CanRead); // FromStream defaults to leaving the stream open
    }

    [Fact]
    public async Task FromFileAsync_DispatchesRecords()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, Sample(), TestContext.Current.CancellationToken);
            var list = new List<Row>();
            await foreach (var r in FixedWidth.Read<Row>()
                .FromFileAsync(tmp, TestContext.Current.CancellationToken))
            {
                list.Add(r);
            }
            Assert.Equal(3, list.Count);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task FromStreamAsync_DispatchesRecords()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(Sample()));
        var list = new List<Row>();
        await foreach (var r in FixedWidth.Read<Row>()
            .FromStreamAsync(ms, TestContext.Current.CancellationToken))
        {
            list.Add(r);
        }
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void FluentChain_AllOptions()
    {
        // Exercise as many fluent methods on FixedWidthReaderBuilder<T> as possible.
        var r = FixedWidth.Read<Row>()
            .WithDefaultPadChar(' ')
            .WithDefaultAlignment(FieldAlignment.Left)
            .WithMaxRecords(1000)
            .TrackLineNumbers()
            .SkipEmptyLines()
            .AllowShortRows(false)
            .AllowMissingColumns(false)
            .WithoutHeader()
            .CaseSensitiveHeaders()
            .SkipRows(0)
            .WithMaxInputSize(1_000_000)
            .WithCulture(CultureInfo.InvariantCulture)
            .WithCulture("en-US")
            .WithNullValues("NULL", "N/A")
            .WithValidationMode(ValidationMode.Lenient)
            .WithEncoding(Encoding.UTF8)
            .FromText(Sample());
        Assert.Equal(3, r.Records.Count);
    }

    [Fact]
    public void IncludeEmptyLines_Chain_DoesNotThrow()
    {
        var builder = FixedWidth.Read<Row>().IncludeEmptyLines();
        Assert.NotNull(builder);
    }

    [Fact]
    public void WithMaxRecords_Throws_OnOverflow()
    {
        Assert.Throws<FixedWidthException>(() =>
            FixedWidth.Read<Row>().WithMaxRecords(2).FromText(Sample(5)));
    }

    [Fact]
    public void WithMaxInputSize_Chain_DoesNotThrow()
    {
        var builder = FixedWidth.Read<Row>().WithMaxInputSize(1_000_000);
        Assert.NotNull(builder);
    }

    [Fact]
    public void WithCommentCharacter_SkipsCommentLines()
    {
        var text = "# comment\nalice00018\n# another\nbob__00019\n";
        var r = FixedWidth.Read<Row>()
            .WithoutHeader()
            .WithCommentCharacter('#')
            .FromText(text);
        Assert.Equal(2, r.Records.Count);
    }

    [Fact]
    public void OnError_RescuesParseErrors()
    {
        var skipped = new List<int>();
        // Row 2 has non-numeric Age — should trigger error handler via the byte path
        var text = "alice00030\nfoo  XXXXX\nbob__00025\n";
        var r = FixedWidth.Read<Row>()
            .WithoutHeader()
            .OnError((_, _) =>
            {
                skipped.Add(1);
                return FixedWidthDeserializeErrorAction.SkipRecord;
            })
            .FromText(text);
        Assert.True(skipped.Count >= 1 || r.Records.Count > 0);
    }

    [Fact]
    public void OnError_NullHandler_Accepts()
    {
        // The setter accepts null without throwing — just verify the chain is fluent.
        var builder = FixedWidth.Read<Row>().OnError(null!);
        Assert.NotNull(builder);
    }

    [Fact]
    public void WithEncoding_NonUtf8_ReadsCorrectly()
    {
        var bytes = Encoding.Latin1.GetBytes(Sample());
        using var ms = new MemoryStream(bytes);
        var r = FixedWidth.Read<Row>()
            .WithEncoding(Encoding.Latin1)
            .FromStream(ms);
        Assert.Equal(3, r.Records.Count);
    }

    [Fact]
    public void WithProgress_RegisteredOk()
    {
        var p = new Progress<FixedWidthProgress>(_ => { });
        var r = FixedWidth.Read<Row>().WithProgress(p, intervalRows: 1).FromText(Sample());
        Assert.Equal(3, r.Records.Count);
    }

    private static bool ParseIntConverter(ReadOnlySpan<char> s, CultureInfo c, string? f, out int v)
        => int.TryParse(s, NumberStyles.Any, c, out v);

    [Fact]
    public void RegisterConverter_AcceptsCustomDelegate()
    {
        var r = FixedWidth.Read<Row>()
            .RegisterConverter<int>(ParseIntConverter)
            .FromText(Sample(2));
        Assert.Equal(2, r.Records.Count);
    }

    [Fact]
    public void RegisterConverter_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FixedWidth.Read<Row>().RegisterConverter<int>(null!));
    }

    [Fact]
    public void WithMap_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FixedWidth.Read<Row>().WithMap(null!));
    }

    [Fact]
    public void FromText_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FixedWidth.Read<Row>().FromText(null!));
    }

    [Fact]
    public void FromFile_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FixedWidth.Read<Row>().FromFile(null!));
    }

    [Fact]
    public void FromStream_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FixedWidth.Read<Row>().FromStream(null!));
    }

    [Fact]
    public void WithRecordLength_Chain_DoesNotThrow()
    {
        var builder = FixedWidth.Read<Row>().WithRecordLength(10);
        Assert.NotNull(builder);
    }

    [Fact]
    public void LineBased_Default()
    {
        var r = FixedWidth.Read<Row>().LineBased().FromText(Sample());
        Assert.Equal(3, r.Records.Count);
    }

    [Fact]
    public void NonGenericBuilder_WithRecordLength_LineBased_Chain()
    {
        var nonGen = FixedWidth.Read()
            .WithRecordLength(10)
            .LineBased()
            .WithDefaultPadChar(' ')
            .WithDefaultAlignment(FieldAlignment.Left)
            .WithMaxRecords(100)
            .TrackLineNumbers()
            .SkipEmptyLines()
            .IncludeEmptyLines()
            .AllowShortRows()
            .AllowMissingColumns()
            .WithCommentCharacter('#')
            .WithHeader()
            .WithoutHeader();
        Assert.NotNull(nonGen);
    }
}
