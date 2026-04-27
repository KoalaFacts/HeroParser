using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives <see cref="HeroParser.SeparatedValues.Reading.Rows.CsvRow{T}"/> behavior
/// (ToStringArray, HasDangerousFields, IsDangerousColumn, Clone) via real CSV reads.
/// Targets the ~58 missing lines on CsvRow.cs (75% baseline).
/// </summary>
[Trait("Category", "Unit")]
public class CsvRowDirectTests
{
    [Fact]
    public void ToStringArray_Char_Returns_All_Columns()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("hello,world,!".AsSpan());
        Assert.True(reader.MoveNext());
        var arr = reader.Current.ToStringArray();
        Assert.Equal(3, arr.Length);
        Assert.Equal("hello", arr[0]);
        Assert.Equal("world", arr[1]);
        Assert.Equal("!", arr[2]);
    }

    [Fact]
    public void ToStringArray_Byte_Returns_All_Columns()
    {
        var bytes = "hello,world,!"u8.ToArray();
        var reader = HeroParser.Csv.ReadFromByteSpan(bytes);
        Assert.True(reader.MoveNext());
        var arr = reader.Current.ToStringArray();
        Assert.Equal(3, arr.Length);
        Assert.Equal("hello", arr[0]);
    }

    [Fact]
    public void GetString_ReturnsTrimmedValue()
    {
        var opts = new CsvReadOptions { TrimFields = true };
        var reader = HeroParser.Csv.ReadFromCharSpan("  hello  ,  world  ".AsSpan(), opts);
        Assert.True(reader.MoveNext());
        Assert.Equal("hello", reader.Current.GetString(0));
    }

    [Fact]
    public void GetString_NoTrim_PreservesWhitespace()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("  hello  ,world".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.Equal("  hello  ", reader.Current.GetString(0));
    }

    [Fact]
    public void HasDangerousFields_DetectsLeadingEqualsChar()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("=cmd|0,safe".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void HasDangerousFields_DetectsLeadingAtChar()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("@formula,safe".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void HasDangerousFields_DetectsLeadingTab()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("\tdanger,safe".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void HasDangerousFields_PlainText_ReturnsFalse()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("hello,world,foo,bar,baz,qux".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void HasDangerousFields_LeadingMinus_FollowedByDigit_NotDangerous()
    {
        // "-42" is a negative number — not dangerous.
        var reader = HeroParser.Csv.ReadFromCharSpan("-42,3.14,hello".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void HasDangerousFields_LeadingPlus_FollowedByDigit_NotDangerous()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("+42,safe,col".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void HasDangerousFields_LeadingMinus_FollowedByLetter_IsDangerous()
    {
        // "-cmd" looks like Excel formula attempt
        var reader = HeroParser.Csv.ReadFromCharSpan("-cmd|0,safe".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void IsDangerousColumn_PerColumnQuery()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("=cmd,safe,@formula".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current.IsDangerousColumn(0));
        Assert.False(reader.Current.IsDangerousColumn(1));
        Assert.True(reader.Current.IsDangerousColumn(2));
    }

    [Fact]
    public void IsDangerousColumn_EmptyColumn_NotDangerous()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan(",,".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current.IsDangerousColumn(0));
        Assert.False(reader.Current.IsDangerousColumn(1));
    }

    [Fact]
    public void HasDangerousFields_LargeRow_TriggersSimdPrescan()
    {
        // > 4 columns triggers the SIMD pre-scan path.
        var sb = new StringBuilder();
        for (int i = 0; i < 20; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("col").Append(i);
        }
        var reader = HeroParser.Csv.ReadFromCharSpan(sb.ToString().AsSpan());
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void HasDangerousFields_LargeRow_ContainingDangerousField()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 20; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(i == 10 ? "=BAD" : "safe");
        }
        var reader = HeroParser.Csv.ReadFromCharSpan(sb.ToString().AsSpan());
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void HasDangerousFields_LargeRow_Bytes()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 20; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("col").Append(i);
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var reader = HeroParser.Csv.ReadFromByteSpan(bytes);
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void HasDangerousFields_LargeRow_Bytes_WithDangerous()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 20; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(i == 5 ? "=danger" : "safe");
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var reader = HeroParser.Csv.ReadFromByteSpan(bytes);
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current.HasDangerousFields());
    }

    [Fact]
    public void IsDangerousColumn_Bytes_DetectsLeadingEquals()
    {
        var bytes = "=cmd,safe"u8.ToArray();
        var reader = HeroParser.Csv.ReadFromByteSpan(bytes);
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current.IsDangerousColumn(0));
        Assert.False(reader.Current.IsDangerousColumn(1));
    }

    [Fact]
    public void IsDangerousColumn_Bytes_LeadingMinusDigit_NotDangerous()
    {
        var bytes = "-42,safe"u8.ToArray();
        var reader = HeroParser.Csv.ReadFromByteSpan(bytes);
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current.IsDangerousColumn(0));
    }

    [Fact]
    public void LineNumber_AndSourceLineNumber_AreConsistent()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("a,b\nc,d\n".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.Equal(1, reader.Current.LineNumber);
        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current.LineNumber);
    }

    [Fact]
    public void ColumnCount_ReflectsCommaCount()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("a,b,c,d,e".AsSpan());
        Assert.True(reader.MoveNext());
        Assert.Equal(5, reader.Current.ColumnCount);
    }

    [Fact]
    public void ToStringArray_RoundTripsManyRows()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("a,b,c\n1,2,3\n4,5,6\n".AsSpan());
        var rows = new List<string[]>();
        while (reader.MoveNext())
        {
            rows.Add(reader.Current.ToStringArray());
        }
        Assert.Equal(3, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["4", "5", "6"], rows[2]);
    }

    [Fact]
    public void IndexerOutOfRange_Throws()
    {
        var reader = HeroParser.Csv.ReadFromCharSpan("a,b".AsSpan());
        Assert.True(reader.MoveNext());
        // ref struct prevents lambda capture; inline try/catch
        try
        {
            _ = reader.Current[10];
            Assert.Fail("Expected IndexOutOfRangeException");
        }
        catch (IndexOutOfRangeException) { }
    }
}
