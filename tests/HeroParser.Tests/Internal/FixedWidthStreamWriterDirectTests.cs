using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Streaming;
using HeroParser.FixedWidths.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Direct tests for the synchronous and asynchronous fixed-width stream writers.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class FixedWidthStreamWriterDirectTests
{
    [Fact]
    public void WriteField_String_PadsRight_ByDefault()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField("hi", 5);
            w.EndRow();
        }
        Assert.StartsWith("hi   ", sw.ToString());
    }

    [Fact]
    public void WriteField_String_RightAlignment_PadsLeft()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField("42", 5, FieldAlignment.Right, '0');
            w.EndRow();
        }
        Assert.StartsWith("00042", sw.ToString());
    }

    [Fact]
    public void WriteField_Span_AppendsField()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField("abc".AsSpan(), 5);
            w.EndRow();
        }
        Assert.StartsWith("abc  ", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_FormatsValue()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(42, 5, FieldAlignment.Right, '0');
            w.EndRow();
        }
        Assert.StartsWith("00042", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_WithFormat_AppliesFormat()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(new DateTime(2024, 1, 15), 8, format: "yyyyMMdd");
            w.EndRow();
        }
        Assert.StartsWith("20240115", sw.ToString());
    }

    [Fact]
    public void WriteField_OverflowTruncate_Default()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField("toolong", 4);
            w.EndRow();
        }
        Assert.StartsWith("tool", sw.ToString());
    }

    [Fact]
    public void WriteField_OverflowThrow_Throws()
    {
        var opts = new FixedWidthWriteOptions { OverflowBehavior = OverflowBehavior.Throw };
        using var sw = new StringWriter();
        using var w = new FixedWidthStreamWriter(sw, opts, leaveOpen: true);
        Assert.Throws<FixedWidthException>(() => w.WriteField("toolong", 4));
    }

    [Fact]
    public void EndRow_AppendsNewLine()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField("a", 1);
            w.EndRow();
            w.WriteField("b", 1);
            w.EndRow();
        }
        var lines = sw.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Flush_PushesData()
    {
        using var sw = new StringWriter();
        using var w = new FixedWidthStreamWriter(sw, leaveOpen: true);
        w.WriteField("x", 1);
        w.EndRow();
        w.Flush();
        Assert.NotEmpty(sw.ToString());
    }

    [Fact]
    public async Task FlushAsync_PushesData()
    {
        using var sw = new StringWriter();
        await using var w = new FixedWidthStreamWriter(sw, leaveOpen: true);
        w.WriteField("x", 1);
        w.EndRow();
        await w.FlushAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(sw.ToString());
    }

    [Fact]
    public void Constructor_NullWriter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FixedWidthStreamWriter(null!));
    }

    [Fact]
    public void UseAfterDispose_Throws()
    {
        using var sw = new StringWriter();
        var w = new FixedWidthStreamWriter(sw, leaveOpen: true);
        w.Dispose();
        Assert.Throws<ObjectDisposedException>(() => w.WriteField("x", 1));
    }

    [Fact]
    public async Task DisposeAsync_Flushes()
    {
        using var sw = new StringWriter();
        var w = new FixedWidthStreamWriter(sw, leaveOpen: true);
        w.WriteField("x", 5);
        w.EndRow();
        await w.DisposeAsync();
        Assert.Contains("x", sw.ToString());
    }

    // ────── async writer ──────

    [Fact]
    public async Task AsyncWriter_WriteFieldAsync_String()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("hi", 5, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.StartsWith("hi   ", text);
    }

    [Fact]
    public async Task AsyncWriter_WriteFieldAsync_RightAligned()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("42", 5, FieldAlignment.Right, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.StartsWith("   42", text);
    }

    [Fact]
    public async Task AsyncWriter_WriteFieldAsync_Object_FormatsValue()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync(42, 5, FieldAlignment.Right, '0', cancellationToken: TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.StartsWith("00042", text);
    }

    [Fact]
    public async Task AsyncWriter_WriteFieldAsync_Memory()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("abc".AsMemory(), 5, FieldAlignment.Left, ' ', TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.StartsWith("abc  ", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task AsyncWriter_FlushAsync_PushesData()
    {
        using var ms = new MemoryStream();
        await using var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true);
        await w.WriteFieldAsync("x", 1, TestContext.Current.CancellationToken);
        await w.EndRowAsync(TestContext.Current.CancellationToken);
        await w.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task AsyncWriter_Constructor_NullStream_Throws()
    {
        await Task.CompletedTask;
        Assert.Throws<ArgumentNullException>(() => new FixedWidthAsyncStreamWriter(null!));
    }

    [Fact]
    public async Task AsyncWriter_UseAfterDispose_Throws()
    {
        using var ms = new MemoryStream();
        var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true);
        await w.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await w.WriteFieldAsync("x", 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AsyncWriter_LeaveOpenFalse_ClosesStream()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: false))
        {
            await w.WriteFieldAsync("x", 1, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.False(ms.CanWrite);
    }

    [Fact]
    public async Task AsyncWriter_OverflowTruncate_Default()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            await w.WriteFieldAsync("toolong", 4, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.StartsWith("tool", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task AsyncWriter_OverflowThrow_Throws()
    {
        var opts = new FixedWidthWriteOptions { OverflowBehavior = OverflowBehavior.Throw };
        using var ms = new MemoryStream();
        await using var w = new FixedWidthAsyncStreamWriter(ms, opts, leaveOpen: true);
        await Assert.ThrowsAsync<FixedWidthException>(
            async () => await w.WriteFieldAsync("toolong", 4, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AsyncWriter_LargeValue_TriggersAsyncSlowPath()
    {
        using var ms = new MemoryStream();
        await using (var w = new FixedWidthAsyncStreamWriter(ms, leaveOpen: true))
        {
            var big = new string('a', 16 * 1024);
            await w.WriteFieldAsync(big, 16 * 1024, TestContext.Current.CancellationToken);
            await w.EndRowAsync(TestContext.Current.CancellationToken);
        }
        Assert.True(ms.Length >= 16 * 1024);
    }
}
