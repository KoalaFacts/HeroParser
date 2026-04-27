using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Direct unit tests for <see cref="CsvAsyncStreamWriter"/> exercising WriteFieldAsync,
/// WriteRowAsync (string + object), EndRowAsync, FlushAsync, options chains, and
/// disposal. The class was at ~55% coverage with 355 missing lines; many of the
/// gaps were the WriteRowAsync fast/slow paths and various option overrides.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvAsyncStreamWriterDirectTests
{
    private static async Task<string> ReadAllAsync(MemoryStream ms)
    {
        ms.Position = 0;
        using var sr = new StreamReader(ms, Encoding.UTF8, leaveOpen: true);
        return await sr.ReadToEndAsync();
    }

    [Fact]
    public async Task WriteFieldAsync_String_AppendsField()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("hello", TestContext.Current.CancellationToken);
            await w.WriteFieldAsync("world", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Contains("hello,world", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task WriteFieldAsync_NullString_WritesEmpty()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(null as string, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var output = await ReadAllAsync(ms);
        Assert.Equal("\r\n", output);
    }

    [Fact]
    public async Task WriteFieldAsync_ReadOnlyMemory_AppendsField()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("abc".AsMemory(), TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Contains("abc", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task WriteRowAsync_StringArray_BasicRow()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync(["a", "b", "c"], TestContext.Current.CancellationToken);
            await w.WriteRowAsync(["1", "2", "3"], TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains("a,b,c", text);
        Assert.Contains("1,2,3", text);
    }

    [Fact]
    public async Task WriteRowAsync_ObjectArray_FormatsValues()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync([42, 3.14, true, "x"], TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains("42", text);
        Assert.Contains("3.14", text);
        Assert.Contains("True", text);
        Assert.Contains("x", text);
    }

    [Fact]
    public async Task WriteRowAsync_ObjectArray_WithNull_DoesNotCrash()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync([null, "x"], TestContext.Current.CancellationToken);
        }
        Assert.Contains("x", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task WriteRowAsync_StringArray_WithNull_DoesNotCrash()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync([null, "x"], TestContext.Current.CancellationToken);
        }
        Assert.Contains("x", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task QuoteWhenNeeded_WrapsFieldsContainingDelimiter()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("has,comma", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Contains("\"has,comma\"", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task QuoteWhenNeeded_EscapesEmbeddedQuote()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("she said \"hi\"", TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.Contains("\"she said \"\"hi\"\"\"", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task AlwaysQuote_WrapsEveryField()
    {
        var opts = new CsvWriteOptions { QuoteStyle = QuoteStyle.Always };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteRowAsync(["a", "b"], TestContext.Current.CancellationToken);
        }
        Assert.Contains("\"a\",\"b\"", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task NeverQuote_DoesNotWrap()
    {
        var opts = new CsvWriteOptions { QuoteStyle = QuoteStyle.Never };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteRowAsync(["plain", "text"], TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.DoesNotContain("\"", text);
    }

    [Fact]
    public async Task CustomDelimiter_UsedInOutput()
    {
        var opts = new CsvWriteOptions { Delimiter = '|' };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteRowAsync(["a", "b"], TestContext.Current.CancellationToken);
        }
        Assert.Contains("a|b", await ReadAllAsync(ms));
    }

    [Fact]
    public async Task CustomNewLine_UsedInOutput()
    {
        var opts = new CsvWriteOptions { NewLine = "\n" };
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true))
        {
            await w.WriteRowAsync(["a"], TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.DoesNotContain("\r\n", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public async Task CharsWritten_IncrementsAsFieldsWrite()
    {
        using var ms = new MemoryStream();
        await using var w = new CsvAsyncStreamWriter(ms, leaveOpen: true);
        var initial = w.CharsWritten;
        await w.WriteFieldAsync("hello", TestContext.Current.CancellationToken);
        Assert.True(w.CharsWritten > initial);
    }

    [Fact]
    public async Task FlushAsync_PushesBufferedData()
    {
        using var ms = new MemoryStream();
        await using var w = new CsvAsyncStreamWriter(ms, leaveOpen: true);
        await w.WriteRowAsync(["a", "b"], TestContext.Current.CancellationToken);
        await w.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task DisposeAsync_FlushesAndCloses_RespectingLeaveOpen()
    {
        var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteRowAsync(["a"], TestContext.Current.CancellationToken);
        }
        Assert.True(ms.CanWrite);

        var ms2 = new MemoryStream();
        await using (var w2 = new CsvAsyncStreamWriter(ms2, leaveOpen: false))
        {
            await w2.WriteRowAsync(["a"], TestContext.Current.CancellationToken);
        }
        Assert.False(ms2.CanWrite);
    }

    [Fact]
    public async Task UseAfterDispose_Throws()
    {
        using var ms = new MemoryStream();
        var w = new CsvAsyncStreamWriter(ms, leaveOpen: true);
        await w.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await w.WriteFieldAsync("x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Constructor_NullStream_Throws()
    {
        await Task.CompletedTask;
        Assert.Throws<ArgumentNullException>(() => new CsvAsyncStreamWriter(null!));
    }

    [Fact]
    public async Task MaxColumnCount_Exceeded_Throws()
    {
        var opts = new CsvWriteOptions { MaxColumnCount = 2 };
        using var ms = new MemoryStream();
        await using var w = new CsvAsyncStreamWriter(ms, opts, leaveOpen: true);
        await Assert.ThrowsAsync<CsvException>(
            async () => await w.WriteRowAsync(["a", "b", "c"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LargeFieldValue_TriggersAsyncSlowPath()
    {
        // Force the async slow path by writing a value larger than the internal char buffer.
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            var big = new string('a', 16 * 1024);
            await w.WriteFieldAsync(big, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains(new string('a', 16 * 1024), text);
    }

    [Fact]
    public async Task ManyRows_BatchedWriteWorks()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, leaveOpen: true))
        {
            for (int i = 0; i < 100; i++)
            {
                await w.WriteRowAsync([i.ToString(), $"name{i}"], TestContext.Current.CancellationToken);
            }
        }
        var text = await ReadAllAsync(ms);
        Assert.Contains("0,name0", text);
        Assert.Contains("99,name99", text);
    }

    [Fact]
    public async Task Encoding_OverridesUtf8()
    {
        using var ms = new MemoryStream();
        await using (var w = new CsvAsyncStreamWriter(ms, options: null, encoding: Encoding.Unicode, leaveOpen: true))
        {
            await w.WriteRowAsync(["abc"], TestContext.Current.CancellationToken);
        }
        // UTF-16 encoded "abc" includes 0x00 padding bytes
        var bytes = ms.ToArray();
        Assert.Contains((byte)0, bytes);
    }
}
