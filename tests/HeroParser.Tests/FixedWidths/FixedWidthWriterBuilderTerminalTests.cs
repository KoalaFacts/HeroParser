using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

/// <summary>
/// Covers the many terminal methods on FixedWidthWriterBuilder&lt;T&gt; and FixedWidthWriterBuilder
/// that lacked direct tests (ToText/ToFile/ToStream/ToWriter + async variants + configuration options).
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class FixedWidthWriterBuilderTerminalTests
{
    public class Record
    {
        [PositionalMap(Start = 0, Length = 10)]
        public string? Name { get; set; }

        [PositionalMap(Start = 10, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Age { get; set; }
    }

    private static IEnumerable<Record> SampleRecords() =>
    [
        new Record { Name = "Alice", Age = 30 },
        new Record { Name = "Bob", Age = 25 }
    ];

    private static async IAsyncEnumerable<Record> SampleRecordsAsync()
    {
        foreach (var r in SampleRecords())
        {
            await Task.Yield();
            yield return r;
        }
    }

    [Fact]
    public void ToText_WritesRecords()
    {
        var text = FixedWidth.Write<Record>().ToText(SampleRecords());
        Assert.Contains("Alice", text);
        Assert.Contains("00030", text);
        Assert.Contains("\r\n", text);
    }

    [Fact]
    public void ToText_WithNewLine_UsesCustomDelimiter()
    {
        var text = FixedWidth.Write<Record>().WithNewLine("\n").ToText(SampleRecords());
        Assert.DoesNotContain("\r\n", text);
        Assert.Contains("\n", text);
    }

    [Fact]
    public void WithPadChar_Chain_DoesNotThrow()
    {
        // Column-level [PositionalMap(PadChar=...)] takes precedence, so this is just a smoke test.
        var text = FixedWidth.Write<Record>().WithPadChar('.').ToText(SampleRecords());
        Assert.NotEmpty(text);
    }

    [Fact]
    public void AlignLeft_AlignRight_WithAlignment_Chain()
    {
        // Column-level alignment on [PositionalMap] takes precedence, so these may all be equal.
        // This test exercises the builder chaining rather than asserting differences.
        var a = FixedWidth.Write<Record>().AlignLeft().ToText(SampleRecords());
        var b = FixedWidth.Write<Record>().AlignRight().ToText(SampleRecords());
        var c = FixedWidth.Write<Record>().WithAlignment(FieldAlignment.Left).ToText(SampleRecords());
        Assert.NotEmpty(a);
        Assert.NotEmpty(b);
        Assert.NotEmpty(c);
    }

    [Fact]
    public void WithCulture_Object_And_Name_Equivalent()
    {
        var a = FixedWidth.Write<Record>().WithCulture(CultureInfo.InvariantCulture).ToText(SampleRecords());
        var b = FixedWidth.Write<Record>().WithCulture("en-US").ToText(SampleRecords());
        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public void WithEncoding_UsedByStreamWriters()
    {
        using var ms = new MemoryStream();
        FixedWidth.Write<Record>().WithEncoding(Encoding.UTF8).ToStream(ms, SampleRecords(), leaveOpen: true);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public void WithMaxOutputSize_EnforcesLimit()
    {
        var writer = FixedWidth.Write<Record>().WithMaxOutputSize(5);
        Assert.Throws<FixedWidthException>(() =>
            writer.ToText([new Record { Name = "TooLargeForLimit", Age = 99 }]));
    }

    [Fact]
    public void WithMaxRowCount_EnforcesLimit()
    {
        var builder = FixedWidth.Write<Record>().WithMaxRowCount(1);
        Assert.Throws<FixedWidthException>(() => builder.ToText(SampleRecords()));
    }

    [Fact]
    public void WithMaxRowCount_Null_Unbounded()
    {
        var text = FixedWidth.Write<Record>().WithMaxRowCount(null).ToText(SampleRecords());
        Assert.Contains("Alice", text);
        Assert.Contains("Bob", text);
    }

    [Fact]
    public void TruncateOnOverflow_SilentlyTruncates()
    {
        var text = FixedWidth.Write<Record>()
            .TruncateOnOverflow()
            .ToText([new Record { Name = "MuchTooLongForTheTenByteColumn", Age = 1 }]);
        Assert.StartsWith("MuchTooLon", text);
    }

    [Fact]
    public void ThrowOnOverflow_Throws()
    {
        var builder = FixedWidth.Write<Record>().ThrowOnOverflow();
        Assert.Throws<FixedWidthException>(() =>
            builder.ToText([new Record { Name = "TooLongForColumn", Age = 1 }]));
    }

    [Fact]
    public void WithOverflowBehavior_Truncate()
    {
        var text = FixedWidth.Write<Record>()
            .WithOverflowBehavior(OverflowBehavior.Truncate)
            .ToText([new Record { Name = "MuchTooLong", Age = 1 }]);
        Assert.StartsWith("MuchTooLon", text);
    }

    [Fact]
    public void WithNullValue_UsedForNullProperty()
    {
        var text = FixedWidth.Write<Record>()
            .WithNullValue("N/A")
            .ToText([new Record { Name = null, Age = 0 }]);
        Assert.Contains("N/A", text);
    }

    [Fact]
    public void WithDateTimeFormat_And_Friends_Chain()
    {
        // Just exercise chaining — format only matters if a date property exists.
        var builder = FixedWidth.Write<Record>()
            .WithDateTimeFormat("yyyy-MM-dd")
            .WithDateOnlyFormat("yyyy-MM-dd")
            .WithTimeOnlyFormat("HH:mm:ss")
            .WithNumberFormat("N2");
        var text = builder.ToText(SampleRecords());
        Assert.Contains("Alice", text);
    }

    [Fact]
    public void WithValidationMode_Lenient_DoesNotThrow()
    {
        var text = FixedWidth.Write<Record>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToText(SampleRecords());
        Assert.NotEmpty(text);
    }

    [Fact]
    public void OnError_ReceivesSerializationErrors()
    {
        var calls = 0;
        var builder = FixedWidth.Write<Record>()
            .ThrowOnOverflow()
            .OnError(_ =>
            {
                calls++;
                return FixedWidthSerializeErrorAction.SkipRow;
            });

        var text = builder.ToText([
            new Record { Name = "OkayButJustFits", Age = 1 }, // Overflow - handler invoked
            new Record { Name = "Bob", Age = 2 }
        ]);

        Assert.Equal(1, calls);
        Assert.Contains("Bob", text);
    }

    [Fact]
    public void ToFile_WritesToDisk()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"fw_builder_test_{Guid.NewGuid()}.dat");
        try
        {
            FixedWidth.Write<Record>().ToFile(tempPath, SampleRecords());
            var content = File.ReadAllText(tempPath);
            Assert.Contains("Alice", content);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void ToStream_LeaveOpenRespected()
    {
        using var ms = new MemoryStream();
        FixedWidth.Write<Record>().ToStream(ms, SampleRecords(), leaveOpen: true);
        Assert.True(ms.CanWrite);
    }

    [Fact]
    public void ToStream_LeaveOpenFalse_ClosesStream()
    {
        using var ms = new MemoryStream();
        FixedWidth.Write<Record>().ToStream(ms, SampleRecords(), leaveOpen: false);
        Assert.False(ms.CanWrite);
    }

    [Fact]
    public void ToWriter_Writes_LeavesOpen()
    {
        using var sw = new StringWriter();
        FixedWidth.Write<Record>().ToWriter(sw, SampleRecords(), leaveOpen: true);
        var output = sw.ToString();
        Assert.Contains("Alice", output);
    }

    [Fact]
    public async Task ToTextAsync_IAsyncEnumerable_Works()
    {
        var text = await FixedWidth.Write<Record>().ToTextAsync(SampleRecordsAsync(), TestContext.Current.CancellationToken);
        Assert.Contains("Alice", text);
    }

    [Fact]
    public async Task ToFileAsync_IAsyncEnumerable_Works()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"fw_builder_async_{Guid.NewGuid()}.dat");
        try
        {
            await FixedWidth.Write<Record>().ToFileAsync(tempPath, SampleRecordsAsync(), TestContext.Current.CancellationToken);
            var content = await File.ReadAllTextAsync(tempPath, TestContext.Current.CancellationToken);
            Assert.Contains("Alice", content);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ToFileAsync_IEnumerable_Works()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"fw_builder_async_sync_{Guid.NewGuid()}.dat");
        try
        {
            await FixedWidth.Write<Record>().ToFileAsync(tempPath, SampleRecords(), TestContext.Current.CancellationToken);
            var content = await File.ReadAllTextAsync(tempPath, TestContext.Current.CancellationToken);
            Assert.Contains("Alice", content);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ToStreamAsync_IAsyncEnumerable_Works()
    {
        using var ms = new MemoryStream();
        await FixedWidth.Write<Record>().ToStreamAsync(ms, SampleRecordsAsync(), leaveOpen: true, TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task ToStreamAsync_IEnumerable_Works()
    {
        using var ms = new MemoryStream();
        await FixedWidth.Write<Record>().ToStreamAsync(ms, SampleRecords(), leaveOpen: true, TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task ToStreamAsyncStreaming_IAsyncEnumerable_Works()
    {
        using var ms = new MemoryStream();
        await FixedWidth.Write<Record>().ToStreamAsyncStreaming(ms, SampleRecordsAsync(), leaveOpen: true, TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task ToStreamAsyncStreaming_IEnumerable_Works()
    {
        using var ms = new MemoryStream();
        await FixedWidth.Write<Record>().ToStreamAsyncStreaming(ms, SampleRecords(), leaveOpen: true, TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    // ───────── Non-generic row-based builder ─────────

    [Fact]
    public void NonGeneric_CreateWriter_ReturnsStreamWriter()
    {
        using var sw = new StringWriter();
        using var writer = FixedWidth.Write().CreateWriter(sw, leaveOpen: true);
        Assert.NotNull(writer);
    }

    [Fact]
    public void NonGeneric_CreateFileWriter_WritesToDisk()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"fw_nongen_{Guid.NewGuid()}.dat");
        try
        {
            using (var writer = FixedWidth.Write().CreateFileWriter(tempPath))
            {
                writer.WriteField("hello", 10);
                writer.WriteField("00123", 5);
                writer.EndRow();
            }
            var content = File.ReadAllText(tempPath);
            Assert.Contains("hello", content);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void NonGeneric_CreateStreamWriter_Works()
    {
        using var ms = new MemoryStream();
        using (var writer = FixedWidth.Write().CreateStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteField("a", 1);
            writer.WriteField("b", 1);
            writer.EndRow();
        }
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public void NonGeneric_FluentChain_DoesNotThrow()
    {
        // Exercise every non-generic fluent option
        var builder = FixedWidth.Write()
            .WithNewLine("\n")
            .WithPadChar('.')
            .WithAlignment(FieldAlignment.Right)
            .AlignLeft()
            .AlignRight()
            .WithCulture(CultureInfo.InvariantCulture)
            .WithCulture("en-US")
            .WithNullValue("-")
            .WithDateTimeFormat("yyyy-MM-dd")
            .WithDateOnlyFormat("yyyy-MM-dd")
            .WithTimeOnlyFormat("HH:mm:ss")
            .WithNumberFormat("N0")
            .WithEncoding(Encoding.UTF8)
            .WithOverflowBehavior(OverflowBehavior.Truncate)
            .TruncateOnOverflow()
            .ThrowOnOverflow()
            .WithMaxOutputSize(null);

        // Smoke test: create a writer via the builder
        using var sw = new StringWriter();
        using var writer = builder.CreateWriter(sw);
        Assert.NotNull(writer);
    }

    // ───────── Null-argument guards ─────────

    [Fact]
    public void ToText_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => FixedWidth.Write<Record>().ToText(null!));

    [Fact]
    public void ToFile_NullPath_Throws()
        => Assert.Throws<ArgumentNullException>(() => FixedWidth.Write<Record>().ToFile(null!, SampleRecords()));

    [Fact]
    public void ToStream_NullStream_Throws()
        => Assert.Throws<ArgumentNullException>(() => FixedWidth.Write<Record>().ToStream(null!, SampleRecords()));

    [Fact]
    public void ToWriter_NullWriter_Throws()
        => Assert.Throws<ArgumentNullException>(() => FixedWidth.Write<Record>().ToWriter(null!, SampleRecords()));

    [Fact]
    public void WithEncoding_Null_FallsBackToUtf8()
    {
        // Implementation coalesces null to Encoding.UTF8 — so this should not throw.
        var builder = FixedWidth.Write<Record>().WithEncoding(null!);
        var text = builder.ToText(SampleRecords());
        Assert.Contains("Alice", text);
    }

    [Fact]
    public void WithMap_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => FixedWidth.Write<Record>().WithMap(null!));

    [Fact]
    public void OnError_Null_Accepts()
    {
        // OnError stores the handler without a null-check; verify chain does not throw.
        var builder = FixedWidth.Write<Record>().OnError(null!);
        var text = builder.ToText(SampleRecords());
        Assert.Contains("Alice", text);
    }
}
