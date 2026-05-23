using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives remaining uncovered branches in CsvAsyncStreamWriter beyond the
/// edge tests already in CsvAsyncStreamWriterEdgeTests: AlwaysQuote on empty
/// fields, AlwaysQuote on long fields (slow path), WriteQuotedFieldWithPrefix
/// (injection protection), MaxFieldSize enforcement, and various alternate
/// QuoteStyle paths.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvAsyncStreamWriterAdditionalCoverageTests
{
    private static async Task<string> ReadAllAsync(MemoryStream ms)
    {
        ms.Position = 0;
        using var sr = new StreamReader(ms, Encoding.UTF8, leaveOpen: true);
        return await sr.ReadToEndAsync();
    }

    [Fact]
    public async Task AlwaysQuote_EmptyField_WrapsAsDoubleQuotes()
    {
        var opts = new CsvWriteOptions { QuoteStyle = QuoteStyle.Always };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteFieldAsync("", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Equal("\"\"\r\n", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task AlwaysQuote_VeryLongField_TriggersSlowPath()
    {
        var opts = new CsvWriteOptions { QuoteStyle = QuoteStyle.Always };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteFieldAsync(new string('a', 32 * 1024), TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.StartsWith("\"a", text);
        Assert.True(text.Length > 32 * 1024);
    }

    [Fact]
    public async Task AlwaysQuote_LongFieldWithEmbeddedQuote_TriggersSlowPath()
    {
        var opts = new CsvWriteOptions { QuoteStyle = QuoteStyle.Always };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            var big = new string('a', 16 * 1024) + "\"" + new string('b', 16 * 1024);
            await w.WriteFieldAsync(big, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains("\"\"", text);
    }

    [Fact]
    public async Task NeverQuote_EmptyField_ProducesEmpty()
    {
        var opts = new CsvWriteOptions { QuoteStyle = QuoteStyle.Never };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteFieldAsync("", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Equal("\r\n", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task InjectionProtection_LargeDangerousField_TriggersSlowPath()
    {
        var opts = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.EscapeWithQuote };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            var dangerous = "=" + new string('a', 32 * 1024);
            await w.WriteFieldAsync(dangerous, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.StartsWith("\"", text);
    }

    [Fact]
    public async Task InjectionProtection_EscapeWithTab_LargeField()
    {
        var opts = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.EscapeWithTab };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            var dangerous = "@" + new string('a', 16 * 1024);
            await w.WriteFieldAsync(dangerous, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Contains("\t", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task InjectionProtection_Sanitize_LargeField()
    {
        var opts = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.Sanitize };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            var dangerous = "+" + new string('a', 16 * 1024);
            await w.WriteFieldAsync(dangerous, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.False(text.StartsWith('+'));
    }

    [Fact]
    public async Task MaxFieldSize_LargeField_Throws()
    {
        var opts = new CsvWriteOptions { MaxFieldSize = 100 };
        using var ms = new MemoryStream();
        await using var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true);
        await Assert.ThrowsAsync<CsvException>(async () =>
            await w.WriteFieldAsync(new string('a', 200),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FieldRequiringQuote_VeryLong_TriggersSlowPath()
    {
        // Field with embedded comma needs quoting; large size triggers slow path.
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            var big = new string('a', 32 * 1024) + ",end";
            await w.WriteFieldAsync(big, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.StartsWith("\"a", text);
    }

    [Fact]
    public async Task WriteRowAsync_StringArray_ManyColumns()
    {
        // Wide row exercises the multi-column write path.
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            var values = new string?[100];
            for (int i = 0; i < 100; i++) values[i] = $"col{i}";
            await w.WriteRowAsync(values, TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains("col0", text);
        Assert.Contains("col99", text);
    }

    [Fact]
    public async Task WriteRowAsync_ObjectArray_ManyMixedTypes()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            var values = new object?[]
            {
                42, 3.14, true, "text", new DateTime(2024, 1, 15),
                9.99m, 9_000_000_000L,
                new Guid("12345678-1234-1234-1234-123456789012"),
                new DateOnly(2024, 1, 15), new TimeOnly(10, 30)
            };
            await w.WriteRowAsync(values, TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains("42", text);
        Assert.Contains("3.14", text);
    }

    [Fact]
    public async Task EmptyField_FollowedByLargeField_SlowPath()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("", TestContext.Current.CancellationToken);
            await w.WriteFieldAsync(new string('x', 16 * 1024), TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.True(text.Length > 16 * 1024);
    }

    [Fact]
    public async Task NumberFormat_Customized()
    {
        var opts = new CsvWriteOptions { NumberFormat = "F2" };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteRowAsync([3.14159], TestContext.Current.CancellationToken);
        }
        Assert.Contains("3.14", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task DateTimeFormat_Customized()
    {
        var opts = new CsvWriteOptions { DateTimeFormat = "yyyyMMdd" };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteRowAsync([new DateTime(2024, 1, 15)], TestContext.Current.CancellationToken);
        }
        Assert.Contains("20240115", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task ManyRows_WithFlush()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            for (int i = 0; i < 500; i++)
            {
                await w.WriteRowAsync([$"row{i}", i.ToString()], TestContext.Current.CancellationToken);
                if (i % 100 == 99)
                {
                    await w.FlushAsync(TestContext.Current.CancellationToken);
                }
            }
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains("row0,0", text);
        Assert.Contains("row499,499", text);
    }
}
