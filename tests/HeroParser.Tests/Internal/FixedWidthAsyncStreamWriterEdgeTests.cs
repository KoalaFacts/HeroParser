using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Streaming;
using HeroParser.FixedWidths.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Additional edge cases for FixedWidthAsyncStreamWriter beyond the basic
/// FixedWidthStreamWriterDirectTests batch: WriteFieldAsync(object) for
/// ISpanFormattable types, large fields triggering buffer growth, the
/// custom padchar/alignment overrides, and various format strings.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class FixedWidthAsyncStreamWriterEdgeTests
{
    [Fact]
    public async Task WriteFieldAsync_Object_DateTime_FormattedDefault()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(new DateTime(2024, 1, 15), 10, format: "yyyy-MM-dd",
                cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.StartsWith("2024-01-15", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task WriteFieldAsync_Object_Decimal_RightAligned()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(123.45m, 10, FieldAlignment.Right, '0',
                cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("123.45", text);
    }

    [Fact]
    public async Task WriteFieldAsync_Object_Guid()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            var g = new Guid("12345678-1234-1234-1234-123456789012");
            await w.WriteFieldAsync(g, 36, cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Contains("12345678", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task WriteFieldAsync_Object_Int_FormattedWithPadding()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(42, 6, FieldAlignment.Right, '0',
                cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.StartsWith("000042", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task WriteFieldAsync_Object_NullValue()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync((object?)null, 5,
                cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.StartsWith("     ", text);
    }

    [Fact]
    public async Task WriteFieldAsync_String_Right_PadsLeft()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("42", 5, FieldAlignment.Right,
                cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.StartsWith("   42", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task WriteFieldAsync_LargeString_TriggersBufferGrowth()
    {
        var big = new string('a', 32 * 1024);
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(big, 32 * 1024, cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.True(ms.Length >= 32 * 1024);
    }

    [Fact]
    public async Task ManyRows_TriggerMultipleFlushes()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            for (int i = 0; i < 200; i++)
            {
                await w.WriteFieldAsync($"row{i}", 10, cancellationToken: TestContext.Current.CancellationToken);
                await w.WriteFieldAsync(i, 5, FieldAlignment.Right, '0',
                    cancellationToken: TestContext.Current.CancellationToken);
                await w.EndRowAsync(TestContext.Current.CancellationToken);
            }
        }
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("row0", text);
        Assert.Contains("row199", text);
    }

    [Fact]
    public async Task EndRowAsync_AppendsConfiguredNewLine()
    {
        var opts = new FixedWidthWriteOptions { NewLine = "\n" };
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteFieldAsync("a", 3, cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.DoesNotContain("\r\n", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public async Task WriteFieldAsync_String_DefaultAlignment_LeftPadsRight()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("hi", 5, cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.StartsWith("hi   ", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task WriteFieldAsync_Object_DateOnly()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(new DateOnly(2024, 1, 15), 10, format: "yyyy-MM-dd",
                cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.StartsWith("2024-01-15", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task WriteFieldAsync_Object_TimeOnly()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(new TimeOnly(10, 30), 5, format: "HH:mm",
                cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.StartsWith("10:30", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task WriteFieldAsync_Memory_LeftPadded()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("abc".AsMemory(), 6, FieldAlignment.Left, ' ',
                TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.StartsWith("abc   ", Encoding.UTF8.GetString(ms.ToArray()));
    }
}
