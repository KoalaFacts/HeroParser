using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Additional edge-case tests for CsvAsyncStreamWriter that the basic CsvAsyncStreamWriterDirectTests
/// don't reach: object types via WriteRowAsync(object[]) for various spanformattable types, the
/// injection-protection async path, very long quoted fields triggering the slow async write path,
/// and the WriteFormattedValueAsync overloads.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvAsyncStreamWriterEdgeTests
{
    private static async Task<string> ReadAllAsync(MemoryStream ms)
    {
        ms.Position = 0;
        using var sr = new StreamReader(ms, Encoding.UTF8, leaveOpen: true);
        return await sr.ReadToEndAsync();
    }

    [Fact]
    public async Task WriteRowAsync_Object_DateTime_Formatted()
    {
        var opts = new CsvWriteOptions { DateTimeFormat = "yyyy-MM-dd" };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteRowAsync([new DateTime(2024, 1, 15)], TestContext.Current.CancellationToken);
        }
        Assert.Contains("2024-01-15", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task WriteRowAsync_Object_Decimal_Formatted()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync([(decimal)9.99], TestContext.Current.CancellationToken);
        }
        Assert.Contains("9.99", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task WriteRowAsync_Object_Guid_Formatted()
    {
        var guid = new Guid("12345678-1234-1234-1234-123456789012");
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync([guid], TestContext.Current.CancellationToken);
        }
        Assert.Contains("12345678", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task WriteRowAsync_Object_DateOnly_TimeOnly_DoesNotCrash()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync([new DateOnly(2024, 1, 15), new TimeOnly(10, 30)],
                TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.NotEmpty(text);
        Assert.Contains("10:30", text);
    }

    [Fact]
    public async Task WriteFieldAsync_FieldNeedingQuotes_LargeValue()
    {
        // Very long quoted field exercises the WriteQuotedFieldSlowAsync path.
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            var bigField = new string('a', 32 * 1024) + ",end"; // contains comma → must be quoted
            await w.WriteFieldAsync(bigField, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.StartsWith("\"", text);
    }

    [Fact]
    public async Task InjectionProtection_LeadingEqualsField_Escaped()
    {
        var opts = new CsvWriteOptions
        {
            InjectionProtection = CsvInjectionProtection.EscapeWithQuote
        };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteFieldAsync("=cmd|0", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        // The output should have a leading quote/escape preventing formula execution
        var text = await ReadAllAsync(ms);
        Assert.NotEmpty(text);
        Assert.StartsWith("\"", text);
    }

    [Fact]
    public async Task InjectionProtection_Sanitize_StripsLeadingChar()
    {
        var opts = new CsvWriteOptions
        {
            InjectionProtection = CsvInjectionProtection.Sanitize
        };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteFieldAsync("=formula", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.DoesNotContain("=formula", text);
        Assert.Contains("formula", text);
    }

    [Fact]
    public async Task InjectionProtection_Reject_Throws()
    {
        var opts = new CsvWriteOptions
        {
            InjectionProtection = CsvInjectionProtection.Reject
        };
        using var ms = new MemoryStream();
        await using var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true);
        await Assert.ThrowsAsync<CsvException>(async () =>
            await w.WriteFieldAsync("=cmd", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InjectionProtection_EscapeWithTab_PrependsTab()
    {
        var opts = new CsvWriteOptions
        {
            InjectionProtection = CsvInjectionProtection.EscapeWithTab
        };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteFieldAsync("=formula", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Contains("\t", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task FieldContainingNewline_Quoted()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("line1\nline2", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains("\"line1\nline2\"", text);
    }

    [Fact]
    public async Task EmptyField_BetweenDelimiters()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("a", TestContext.Current.CancellationToken);
            await w.WriteFieldAsync("", TestContext.Current.CancellationToken);
            await w.WriteFieldAsync("c", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Contains("a,,c", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task ManyConsecutiveSmallFields()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            for (int i = 0; i < 100; i++)
            {
                await w.WriteFieldAsync(i.ToString(), TestContext.Current.CancellationToken);
            }
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains("0,1,2", text);
        Assert.Contains("99\r\n", text);
    }

    [Fact]
    public async Task WriteRowAsync_LargeObjectArray_SlowPath()
    {
        // Large enough to overflow the sync fast path
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            var values = new object?[] { new string('a', 8192), new string('b', 8192), new string('c', 8192) };
            await w.WriteRowAsync(values, TestContext.Current.CancellationToken);
        }
        Assert.True((await ReadAllAsync(ms)).Length > 24000);
    }

    [Fact]
    public async Task FlushAsync_OnEmptyWriter_NoOp()
    {
        using var ms = new MemoryStream();
        await using var w = new CsvAsyncStreamWriter(ms, leaveOpen: true);
        await w.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, ms.Length);
    }

    [Fact]
    public async Task EndRowAsync_WithoutFields_StillWritesNewline()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Equal("\r\n", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task WriteFieldAsync_Empty_BetweenDelimiters()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(string.Empty, TestContext.Current.CancellationToken);
            await w.WriteFieldAsync("x", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Contains(",x", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task WriteRowAsync_StringArray_SingleColumn()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync(["lonely"], TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.Equal("lonely\r\n", text);
    }

    [Fact]
    public async Task WriteRowAsync_ObjectArray_SingleColumn()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync([42], TestContext.Current.CancellationToken);
        }
        Assert.Equal("42\r\n", await ReadAllAsync(ms));
    }
}
