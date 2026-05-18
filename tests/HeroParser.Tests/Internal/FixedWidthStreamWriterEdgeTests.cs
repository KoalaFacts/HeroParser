using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Additional edge cases for the synchronous FixedWidthStreamWriter beyond the
/// FixedWidthStreamWriterDirectTests batch: WriteField(object) for ISpanFormattable
/// types, large fields, custom alignment + padchar overrides, format strings,
/// and the WriteFormattedValue slow paths.
/// </summary>
[Trait("Category", "Unit")]
public class FixedWidthStreamWriterEdgeTests
{
    [Fact]
    public void WriteField_Object_DateTime_Formatted()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(new DateTime(2024, 1, 15), 10, format: "yyyy-MM-dd");
            w.EndRow();
        }
        Assert.StartsWith("2024-01-15", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_Decimal_RightAligned()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(123.45m, 10, FieldAlignment.Right, '0');
            w.EndRow();
        }
        Assert.Contains("123.45", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_Guid()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            var g = new Guid("12345678-1234-1234-1234-123456789012");
            w.WriteField(g, 36);
            w.EndRow();
        }
        Assert.Contains("12345678", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_Int_FormattedWithPadding()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(42, 6, FieldAlignment.Right, '0');
            w.EndRow();
        }
        Assert.StartsWith("000042", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_NullValue_PadsBlank()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField((object?)null, 5);
            w.EndRow();
        }
        Assert.StartsWith("     ", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_DateOnly()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(new DateOnly(2024, 1, 15), 10, format: "yyyy-MM-dd");
            w.EndRow();
        }
        Assert.StartsWith("2024-01-15", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_TimeOnly()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(new TimeOnly(10, 30), 5, format: "HH:mm");
            w.EndRow();
        }
        Assert.StartsWith("10:30", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_Bool()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(true, 5);
            w.EndRow();
        }
        Assert.StartsWith("True ", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_Long()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(9_000_000_000L, 12, FieldAlignment.Right, '0');
            w.EndRow();
        }
        Assert.StartsWith("009000000000", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_Float()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(3.14f, 8);
            w.EndRow();
        }
        Assert.Contains("3.14", sw.ToString());
    }

    [Fact]
    public void WriteField_Object_Double()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(2.71828, 10);
            w.EndRow();
        }
        Assert.Contains("2.71828", sw.ToString());
    }

    [Fact]
    public void WriteField_LargeString_TriggersBufferGrowth()
    {
        var big = new string('a', 32 * 1024);
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField(big, 32 * 1024);
            w.EndRow();
        }
        Assert.True(sw.ToString().Length >= 32 * 1024);
    }

    [Fact]
    public void ManyRows_TriggerMultipleFlushes()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            for (int i = 0; i < 200; i++)
            {
                w.WriteField($"row{i}", 10);
                w.WriteField(i, 5, FieldAlignment.Right, '0');
                w.EndRow();
            }
        }
        var text = sw.ToString();
        Assert.Contains("row0", text);
        Assert.Contains("row199", text);
    }

    [Fact]
    public void WriteField_Span_LeftAlignment()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField("abc".AsSpan(), 6, FieldAlignment.Left, ' ');
            w.EndRow();
        }
        Assert.StartsWith("abc   ", sw.ToString());
    }

    [Fact]
    public void WriteField_Span_RightAlignment()
    {
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, leaveOpen: true))
        {
            w.WriteField("42".AsSpan(), 5, FieldAlignment.Right, '0');
            w.EndRow();
        }
        Assert.StartsWith("00042", sw.ToString());
    }

    [Fact]
    public void EndRow_AppendsConfiguredNewLine()
    {
        var opts = new FixedWidthWriteOptions { NewLine = "\n" };
        using var sw = new StringWriter();
        using (var w = new FixedWidthStreamWriter(sw, opts, leaveOpen: true))
        {
            w.WriteField("a", 3);
            w.EndRow();
        }
        Assert.DoesNotContain("\r\n", sw.ToString());
        Assert.EndsWith("\n", sw.ToString());
    }
}
