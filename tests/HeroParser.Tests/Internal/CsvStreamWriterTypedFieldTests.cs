using System.Globalization;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Exercises <see cref="CsvStreamWriter"/>'s typed WriteField overloads and the
/// column-count guard on each.
///
/// The string and span overloads were well covered, but the numeric, boolean,
/// DateTime and Guid ones — which format onto a stack buffer instead of allocating —
/// were never called, so each sat entirely uncovered along with its
/// TooManyColumnsWritten guard.
/// </summary>
[Trait("Category", "Unit")]
public class CsvStreamWriterTypedFieldTests
{
    private static string Write(Action<CsvStreamWriter> write, CsvWriteOptions? options = null)
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        using (var w = new CsvStreamWriter(sw, options, leaveOpen: true))
        {
            write(w);
            w.EndRow();
        }
        return sw.ToString().TrimEnd('\r', '\n');
    }

    [Fact]
    public void WriteField_Int_Formats() => Assert.Equal("42", Write(w => w.WriteField(42)));

    [Fact]
    public void WriteField_Long_Formats() => Assert.Equal("9007199254740993", Write(w => w.WriteField(9007199254740993L)));

    [Fact]
    public void WriteField_Double_Formats() => Assert.Equal("1.5", Write(w => w.WriteField(1.5d)));

    [Fact]
    public void WriteField_Float_Formats() => Assert.Equal("2.5", Write(w => w.WriteField(2.5f)));

    [Fact]
    public void WriteField_Decimal_Formats() => Assert.Equal("3.25", Write(w => w.WriteField(3.25m)));

    [Fact]
    public void WriteField_Bool_Formats() => Assert.Equal(true.ToString(CultureInfo.InvariantCulture), Write(w => w.WriteField(true)));

    [Fact]
    public void WriteField_Guid_Formats()
    {
        var value = new Guid("11112222-3333-4444-5555-666677778888");
        Assert.Equal(value.ToString(), Write(w => w.WriteField(value)));
    }

    [Fact]
    public void WriteField_DateTime_Formats()
    {
        string written = Write(w => w.WriteField(new DateTime(2024, 3, 17, 8, 30, 0, DateTimeKind.Unspecified)));
        Assert.Contains("2024", written, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteField_Typed_SeparatesWithDelimiter()
    {
        string row = Write(w =>
        {
            w.WriteField(1);
            w.WriteField(2L);
            w.WriteField(3.5d);
            w.WriteField(true);
        });
        Assert.Equal($"1,2,3.5,{true.ToString(CultureInfo.InvariantCulture)}", row);
    }

    // Each typed overload carries its own column-count guard, so the limit is proven
    // per overload rather than once for the class.
    public static TheoryData<string, Action<CsvStreamWriter>> OverflowingWrites() => new()
    {
        { "int", w => w.WriteField(2) },
        { "long", w => w.WriteField(2L) },
        { "double", w => w.WriteField(2d) },
        { "float", w => w.WriteField(2f) },
        { "decimal", w => w.WriteField(2m) },
        { "bool", w => w.WriteField(false) },
        { "DateTime", w => w.WriteField(DateTime.UnixEpoch) },
        { "Guid", w => w.WriteField(Guid.Empty) },
        { "string", w => w.WriteField("x") },
    };

    [Theory]
    [MemberData(nameof(OverflowingWrites))]
    public void WriteField_BeyondMaxColumnCount_Throws(string label, Action<CsvStreamWriter> writeSecond)
    {
        Assert.False(string.IsNullOrEmpty(label));
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        using var w = new CsvStreamWriter(sw, new CsvWriteOptions { MaxColumnCount = 1 }, leaveOpen: true);

        w.WriteField("first");
        var ex = Assert.Throws<CsvException>(() => writeSecond(w));
        Assert.Equal(CsvErrorCode.TooManyColumnsWritten, ex.ErrorCode);
    }

    [Fact]
    public void WriteField_AtMaxColumnCount_DoesNotThrow()
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        using (var w = new CsvStreamWriter(sw, new CsvWriteOptions { MaxColumnCount = 2 }, leaveOpen: true))
        {
            w.WriteField(1);
            w.WriteField(2);
            w.EndRow();
        }
        Assert.Equal("1,2", sw.ToString().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void WriteField_ColumnCountResetsEachRow()
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        using (var w = new CsvStreamWriter(sw, new CsvWriteOptions { MaxColumnCount = 2 }, leaveOpen: true))
        {
            w.WriteField(1);
            w.WriteField(2);
            w.EndRow();
            // The counter must reset, or a second full row would trip the guard.
            w.WriteField(3);
            w.WriteField(4);
            w.EndRow();
        }
        string[] rows = sw.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["1,2", "3,4"], rows);
    }
}
