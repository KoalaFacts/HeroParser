using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Direct tests for the synchronous <see cref="CsvStreamWriter"/>. The class was at 77%
/// with 105 missing lines, mostly across the WriteField/WriteRow overloads, options
/// chains, and disposal behavior.
/// </summary>
[Trait("Category", "Unit")]
public class CsvStreamWriterDirectTests
{
    private static (StringWriter, CsvStreamWriter) Create(CsvWriteOptions? opts = null, bool leaveOpen = true)
    {
        // sw is returned to the caller, which owns disposal — don't 'using' here.
        var sw = new StringWriter();
        return (sw, new CsvStreamWriter(sw, opts, leaveOpen));
    }

    [Fact]
    public void WriteField_String_AppendsField()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteField("hello");
            w.WriteField("world");
            w.EndRow();
        }
        Assert.Contains("hello,world", sw.ToString());
    }

    [Fact]
    public void WriteField_NullString_WritesEmpty()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteField(null);
            w.EndRow();
        }
        Assert.Equal("\r\n", sw.ToString());
    }

    [Fact]
    public void WriteField_Span_AppendsField()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteField("abc".AsSpan());
            w.EndRow();
        }
        Assert.Contains("abc", sw.ToString());
    }

    [Fact]
    public void WriteRow_StringArray_BasicRow()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteRow("a", "b", "c");
        }
        Assert.Equal("a,b,c\r\n", sw.ToString());
    }

    [Fact]
    public void WriteRow_ObjectArray_FormatsValues()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteRow(42, 3.14, true);
        }
        var output = sw.ToString();
        Assert.Contains("42", output);
        Assert.Contains("3.14", output);
        Assert.Contains("True", output);
    }

    [Fact]
    public void WriteRow_ObjectArray_WithNull_DoesNotCrash()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteRow(default(object?), "x");
        }
        Assert.Contains("x", sw.ToString());
    }

    [Fact]
    public void QuoteWhenNeeded_WrapsCommaField()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteField("has,comma");
            w.EndRow();
        }
        Assert.Contains("\"has,comma\"", sw.ToString());
    }

    [Fact]
    public void QuoteWhenNeeded_EscapesEmbeddedQuote()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteField("she said \"hi\"");
            w.EndRow();
        }
        Assert.Contains("\"she said \"\"hi\"\"\"", sw.ToString());
    }

    [Fact]
    public void AlwaysQuote_WrapsEveryField()
    {
        var (sw, w) = Create(new CsvWriteOptions { QuoteStyle = QuoteStyle.Always });
        using (w)
        {
            w.WriteRow("a", "b");
        }
        Assert.Contains("\"a\",\"b\"", sw.ToString());
    }

    [Fact]
    public void NeverQuote_NoQuoteCharsInOutput()
    {
        var (sw, w) = Create(new CsvWriteOptions { QuoteStyle = QuoteStyle.Never });
        using (w)
        {
            w.WriteRow("a", "b");
        }
        Assert.DoesNotContain("\"", sw.ToString());
    }

    [Fact]
    public void CustomDelimiter_UsedInOutput()
    {
        var (sw, w) = Create(new CsvWriteOptions { Delimiter = '|' });
        using (w)
        {
            w.WriteRow("a", "b");
        }
        Assert.Contains("a|b", sw.ToString());
    }

    [Fact]
    public void CustomNewLine_UsedInOutput()
    {
        var (sw, w) = Create(new CsvWriteOptions { NewLine = "\n" });
        using (w)
        {
            w.WriteRow("a");
        }
        var output = sw.ToString();
        Assert.DoesNotContain("\r\n", output);
        Assert.EndsWith("\n", output);
    }

    [Fact]
    public void CharsWritten_IncrementsAsFieldsWrite()
    {
        var (_, w) = Create();
        using (w)
        {
            var initial = w.CharsWritten;
            w.WriteField("hello");
            Assert.True(w.CharsWritten > initial);
        }
    }

    [Fact]
    public void Flush_PushesBufferedData()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteRow("a", "b");
            w.Flush();
            Assert.NotEmpty(sw.ToString());
        }
    }

    [Fact]
    public async Task FlushAsync_PushesBufferedData()
    {
        var (sw, w) = Create();
        await using (w)
        {
            w.WriteRow("a", "b");
            await w.FlushAsync(TestContext.Current.CancellationToken);
            Assert.NotEmpty(sw.ToString());
        }
    }

    [Fact]
    public void Dispose_RespectsLeaveOpen()
    {
        using var sw = new StringWriter();
        var w = new CsvStreamWriter(sw, leaveOpen: true);
        w.WriteRow("a");
        w.Dispose();
        // Should be able to keep using the StringWriter
        sw.Write("after");
        Assert.Contains("after", sw.ToString());
    }

    [Fact]
    public void UseAfterDispose_Throws()
    {
        var (_, w) = Create();
        w.Dispose();
        Assert.Throws<ObjectDisposedException>(() => w.WriteField("x"));
    }

    [Fact]
    public void Constructor_NullWriter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CsvStreamWriter(null!));
    }

    [Fact]
    public void MaxColumnCount_Exceeded_OnWriteField_Throws()
    {
        var (_, w) = Create(new CsvWriteOptions { MaxColumnCount = 2 });
        using (w)
        {
            w.WriteField("a");
            w.WriteField("b");
            Assert.Throws<CsvException>(() => w.WriteField("c"));
        }
    }

    [Fact]
    public void MaxColumnCount_Exceeded_OnWriteRow_Throws()
    {
        var (_, w) = Create(new CsvWriteOptions { MaxColumnCount = 2 });
        using (w)
        {
            Assert.Throws<CsvException>(() => w.WriteRow("a", "b", "c"));
        }
    }

    [Fact]
    public void WriteRow_SpanOfObjects_Works()
    {
        var (sw, w) = Create();
        using (w)
        {
            object?[] arr = [1, "x", true];
            w.WriteRow((ReadOnlySpan<object?>)arr);
        }
        var output = sw.ToString();
        Assert.Contains("1", output);
        Assert.Contains("x", output);
        Assert.Contains("True", output);
    }

    [Fact]
    public void EndRow_StartsNewRow()
    {
        var (sw, w) = Create();
        using (w)
        {
            w.WriteField("row1");
            w.EndRow();
            w.WriteField("row2");
            w.EndRow();
        }
        var lines = sw.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task DisposeAsync_FlushesAndCloses()
    {
        using var sw = new StringWriter();
        var w = new CsvStreamWriter(sw, leaveOpen: true);
        w.WriteRow("a", "b");
        await w.DisposeAsync();
        Assert.Contains("a,b", sw.ToString());
    }
}
