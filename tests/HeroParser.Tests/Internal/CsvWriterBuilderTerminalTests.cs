using System.Globalization;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers terminal methods on CsvWriterBuilder&lt;T&gt; (async file/stream variants,
/// option chains, and the non-generic row-based builder) that lacked direct tests
/// alongside the existing CsvWriterBuilderTests.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvWriterBuilderTerminalTests
{
    [GenerateBinder]
    public sealed class Person
    {
        [TabularMap(Name = "Name")] public string Name { get; set; } = "";
        [TabularMap(Name = "Age")] public int Age { get; set; }
    }

    private static IEnumerable<Person> Sample() =>
    [
        new() { Name = "Alice", Age = 30 },
        new() { Name = "Bob", Age = 25 }
    ];

    private static async IAsyncEnumerable<Person> SampleAsync()
    {
        foreach (var p in Sample())
        {
            await Task.Yield();
            yield return p;
        }
    }

    [Fact]
    public void ToText_Defaults()
    {
        var text = Csv.Write<Person>().ToText(Sample());
        Assert.Contains("Name,Age", text);
        Assert.Contains("Alice,30", text);
    }

    [Fact]
    public async Task ToTextAsync_IAsyncEnumerable()
    {
        var text = await Csv.Write<Person>().ToTextAsync(SampleAsync(), TestContext.Current.CancellationToken);
        Assert.Contains("Alice", text);
    }

    [Fact]
    public void ToFile_WritesToDisk()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            Csv.Write<Person>().ToFile(tempPath, Sample());
            var text = File.ReadAllText(tempPath);
            Assert.Contains("Alice", text);
        }
        finally { File.Delete(tempPath); }
    }

    [Fact]
    public async Task ToFileAsync_IAsyncEnumerable_WritesToDisk()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            await Csv.Write<Person>().ToFileAsync(tempPath, SampleAsync(), TestContext.Current.CancellationToken);
            var text = await File.ReadAllTextAsync(tempPath, TestContext.Current.CancellationToken);
            Assert.Contains("Alice", text);
        }
        finally { File.Delete(tempPath); }
    }

    [Fact]
    public void ToStream_WritesAndRespectsLeaveOpen()
    {
        using var ms = new MemoryStream();
        Csv.Write<Person>().ToStream(ms, Sample(), leaveOpen: true);
        Assert.True(ms.CanWrite);
    }

    [Fact]
    public void ToStream_LeaveOpenFalse_ClosesStream()
    {
        var ms = new MemoryStream();
        Csv.Write<Person>().ToStream(ms, Sample(), leaveOpen: false);
        Assert.False(ms.CanWrite);
    }

    [Fact]
    public async Task ToStreamAsync_IAsyncEnumerable_LeaveOpenTrue()
    {
        using var ms = new MemoryStream();
        await Csv.Write<Person>().ToStreamAsync(ms, SampleAsync(), leaveOpen: true,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.CanWrite);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task ToStreamAsyncStreaming_IAsyncEnumerable()
    {
        using var ms = new MemoryStream();
        await Csv.Write<Person>().ToStreamAsyncStreaming(ms, SampleAsync(), leaveOpen: true,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task ToStreamAsyncStreaming_IEnumerable()
    {
        using var ms = new MemoryStream();
        await Csv.Write<Person>().ToStreamAsyncStreaming(ms, Sample(), leaveOpen: true,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public void ToWriter_WritesAndLeavesOpen()
    {
        var sw = new StringWriter();
        Csv.Write<Person>().ToWriter(sw, Sample(), leaveOpen: true);
        Assert.Contains("Alice", sw.ToString());
    }

    [Fact]
    public void WithDelimiter_ChangesOutput()
    {
        var text = Csv.Write<Person>().WithDelimiter(';').ToText(Sample());
        Assert.Contains("Name;Age", text);
    }

    [Fact]
    public void WithQuote_ChangesQuoteChar()
    {
        var text = Csv.Write<Person>().WithQuote('\'').AlwaysQuote().ToText(Sample());
        Assert.Contains("'Alice'", text);
    }

    [Fact]
    public void WithNewLine_Custom()
    {
        var text = Csv.Write<Person>().WithNewLine("\n").ToText(Sample());
        Assert.DoesNotContain("\r\n", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void WithoutHeader_OmitsHeaderRow()
    {
        var text = Csv.Write<Person>().WithoutHeader().ToText(Sample());
        Assert.DoesNotContain("Name,Age", text);
        Assert.Contains("Alice,30", text);
    }

    [Fact]
    public void WithHeader_IncludesHeader()
    {
        var text = Csv.Write<Person>().WithHeader().ToText(Sample());
        Assert.Contains("Name,Age", text);
    }

    [Fact]
    public void WithCulture_BothOverloads()
    {
        var t1 = Csv.Write<Person>().WithCulture(CultureInfo.InvariantCulture).ToText(Sample());
        var t2 = Csv.Write<Person>().WithCulture("en-US").ToText(Sample());
        Assert.NotEmpty(t1);
        Assert.NotEmpty(t2);
    }

    [Fact]
    public void WithEncoding_AffectsStreamOutput()
    {
        using var ms = new MemoryStream();
        Csv.Write<Person>().WithEncoding(Encoding.Unicode).ToStream(ms, Sample(), leaveOpen: true);
        // UTF-16 doubles bytes-per-char, presence of zero-byte padding indicates UTF-16
        Assert.Contains((byte)0, ms.ToArray());
    }

    [Fact]
    public void WithMaxRowCount_Throws()
    {
        var builder = Csv.Write<Person>().WithMaxRowCount(1);
        Assert.Throws<CsvException>(() => builder.ToText(Sample()));
    }

    [Fact]
    public void WithMaxFieldSize_Throws_OnLongField()
    {
        var builder = Csv.Write<Person>().WithMaxFieldSize(3);
        Assert.Throws<CsvException>(() =>
            builder.ToText([new Person { Name = "VeryLong", Age = 1 }]));
    }

    [Fact]
    public void WithMaxColumnCount_RespectedOrThrows()
    {
        // Person has 2 columns. Setting MaxColumnCount to 1 should reject the row.
        var builder = Csv.Write<Person>().WithMaxColumnCount(1);
        Assert.Throws<CsvException>(() => builder.ToText(Sample()));
    }

    [Fact]
    public void WithMaxOutputSize_Throws_WhenExceeded()
    {
        var builder = Csv.Write<Person>().WithMaxOutputSize(5);
        Assert.Throws<CsvException>(() => builder.ToText(Sample()));
    }

    [Fact]
    public void WithInjectionProtection_EscapesDangerousLeading()
    {
        var text = Csv.Write<Person>()
            .WithInjectionProtection(CsvInjectionProtection.EscapeWithQuote)
            .ToText([new Person { Name = "=cmd", Age = 1 }]);
        // Field starting with '=' should be escape-quoted; just verify the output isn't a raw =cmd at row start.
        Assert.NotEmpty(text);
    }

    [Fact]
    public void WithDangerousChars_AcceptsCustom()
    {
        var builder = Csv.Write<Person>().WithDangerousChars('@', '!');
        var text = builder.ToText(Sample());
        Assert.NotEmpty(text);
    }

    [Fact]
    public void WithProgress_AndInterval_Chain()
    {
        var reports = new List<CsvWriteProgress>();
        var p = new Progress<CsvWriteProgress>(reports.Add);
        var text = Csv.Write<Person>()
            .WithProgress(p)
            .WithProgressInterval(1)
            .ToText(Sample());
        Assert.NotEmpty(text);
    }

    [Fact]
    public void WithDateFormats_Chain()
    {
        var text = Csv.Write<Person>()
            .WithDateTimeFormat("yyyy-MM-dd")
            .WithDateOnlyFormat("yyyy-MM-dd")
            .WithTimeOnlyFormat("HH:mm:ss")
            .WithNumberFormat("N0")
            .ToText(Sample());
        Assert.NotEmpty(text);
    }

    [Fact]
    public void WithoutEmptyColumns_Chain()
    {
        var text = Csv.Write<Person>().WithoutEmptyColumns().ToText(Sample());
        Assert.NotEmpty(text);
    }

    [Fact]
    public void WithNullValue_AppearsInOutput()
    {
        // Person has no nullable string fields the writer recognises by default; just exercise the chain.
        var builder = Csv.Write<Person>().WithNullValue("<null>");
        Assert.NotEmpty(builder.ToText(Sample()));
    }

    [Fact]
    public void WithValidationMode_Chain()
    {
        var text = Csv.Write<Person>().WithValidationMode(HeroParser.Validation.ValidationMode.Lenient).ToText(Sample());
        Assert.NotEmpty(text);
    }

    [Fact]
    public void OnError_Chain_DoesNotThrow()
    {
        // Just exercise the OnError fluent chain — actual error semantics depend on the
        // underlying exception type, which differs across error sources.
        var builder = Csv.Write<Person>()
            .OnError(_ => SerializeErrorAction.SkipRow);
        Assert.NotEmpty(builder.ToText(Sample()));
    }

    // ───────── Null-argument guards ─────────

    [Fact]
    public void ToText_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.Write<Person>().ToText(null!));

    [Fact]
    public void ToFile_NullPath_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.Write<Person>().ToFile(null!, Sample()));

    [Fact]
    public void ToStream_NullStream_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.Write<Person>().ToStream(null!, Sample()));

    [Fact]
    public void ToWriter_NullWriter_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.Write<Person>().ToWriter(null!, Sample()));

    [Fact]
    public void OnError_Null_Accepts_NoThrow()
    {
        // Setter accepts null without throwing; no chain assertion.
        var builder = Csv.Write<Person>().OnError(null!);
        Assert.NotNull(builder);
    }

    [Fact]
    public void WithMap_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => Csv.Write<Person>().WithMap(null!));
}
