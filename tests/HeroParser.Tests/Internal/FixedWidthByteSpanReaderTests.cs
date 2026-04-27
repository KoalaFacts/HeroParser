using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the FixedWidthByteSpanReader paths via FixedWidth.ReadFromUtf8ByteSpan:
/// BOM stripping, SkipRows, HasHeaderRow, fixed-length records, line-tracking,
/// MaxRecordCount enforcement, and end-of-input edge cases.
/// </summary>
[Trait("Category", "Unit")]
public class FixedWidthByteSpanReaderTests
{
    private static int CountRecords(byte[] bytes, FixedWidthReadOptions options)
    {
        var reader = HeroParser.FixedWidth.ReadFromUtf8ByteSpan(bytes, options);
        int n = 0;
        while (reader.MoveNext()) n++;
        return n;
    }

    private static byte[] MakeUtf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void BomStripped_FromHeaderlessInput()
    {
        // BOM (0xEF 0xBB 0xBF) followed by data
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(MakeUtf8("alice00030\nbob__00025\n"))
            .ToArray();
        Assert.Equal(2, CountRecords(bytes, new FixedWidthReadOptions()));
    }

    [Fact]
    public void SkipRows_HonoredViaUtf8ByteSpan()
    {
        var bytes = MakeUtf8("skip\nname100001\nname200002\n");
        Assert.Equal(2, CountRecords(bytes, new FixedWidthReadOptions { SkipRows = 1 }));
    }

    [Fact]
    public void SkipRows_LineDelimited()
    {
        var bytes = MakeUtf8("ignore\nname100001\nname200002\n");
        Assert.Equal(2, CountRecords(bytes, new FixedWidthReadOptions { SkipRows = 1 }));
    }

    [Fact]
    public void SkipRows_FixedLength()
    {
        // Each row is exactly 10 bytes
        var bytes = MakeUtf8("ignoreee01" + "name100001" + "name200002");
        Assert.Equal(2, CountRecords(bytes, new FixedWidthReadOptions { SkipRows = 1, RecordLength = 10 }));
    }

    [Fact]
    public void HasHeaderRow_SkipsFirst()
    {
        var bytes = MakeUtf8("header\nname100001\nname200002\n");
        Assert.Equal(2, CountRecords(bytes, new FixedWidthReadOptions { HasHeaderRow = true }));
    }

    [Fact]
    public void TrackSourceLineNumbers_LineDelimited()
    {
        var opts = new FixedWidthReadOptions { TrackSourceLineNumbers = true };
        var bytes = MakeUtf8("name100001\nname200002\nname300003\n");
        Assert.Equal(3, CountRecords(bytes, opts));
    }

    [Fact]
    public void TrackSourceLineNumbers_FixedLength()
    {
        var opts = new FixedWidthReadOptions { RecordLength = 10, TrackSourceLineNumbers = true };
        var bytes = MakeUtf8("name100001name200002name300003");
        Assert.Equal(3, CountRecords(bytes, opts));
    }

    [Fact]
    public void TrackSourceLineNumbers_FixedLengthWithEmbeddedNewlines()
    {
        // 12 bytes total with embedded newlines, RecordLength=6 → 2 records
        var opts = new FixedWidthReadOptions { RecordLength = 6, TrackSourceLineNumbers = true };
        var bytes = MakeUtf8("ab\ncd\nef\nh1\n");
        Assert.Equal(2, CountRecords(bytes, opts));
    }

    [Fact]
    public void MaxRecordCount_ExceededThrows()
    {
        var opts = new FixedWidthReadOptions { MaxRecordCount = 2, RecordLength = 10 };
        var bytes = MakeUtf8("name100001name200002name300003");
        Assert.Throws<FixedWidthException>(() => CountRecords(bytes, opts));
    }

    [Fact]
    public void EndOfInputWithoutNewline_LastRowIncluded()
    {
        var bytes = MakeUtf8("name100001\nname200002");
        Assert.Equal(2, CountRecords(bytes, new FixedWidthReadOptions()));
    }

    [Fact]
    public void CrLf_RowSeparator()
    {
        var bytes = MakeUtf8("name100001\r\nname200002\r\nname300003\r\n");
        Assert.Equal(3, CountRecords(bytes, new FixedWidthReadOptions()));
    }

    [Fact]
    public void EmptyInput_NoRows()
    {
        var bytes = Array.Empty<byte>();
        Assert.Equal(0, CountRecords(bytes, new FixedWidthReadOptions()));
    }

    [Fact]
    public void OnlyBom_NoRows()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF };
        Assert.Equal(0, CountRecords(bytes, new FixedWidthReadOptions { RecordLength = 5 }));
    }

    [Fact]
    public void FixedLength_ExactMultiple_AllRowsRead()
    {
        var opts = new FixedWidthReadOptions { RecordLength = 10 };
        // 3 exact rows
        var bytes = MakeUtf8("name100001name200002name300003");
        Assert.Equal(3, CountRecords(bytes, opts));
    }

    [Fact]
    public void SkipRows_RunsOutBeforeData()
    {
        // SkipRows asks to skip more rows than exist - should run out gracefully
        var opts = new FixedWidthReadOptions { SkipRows = 100 };
        var bytes = MakeUtf8("name100001\n");
        Assert.Equal(0, CountRecords(bytes, opts));
    }

    [Fact]
    public void HasHeader_AndSkipRows_BothApplied()
    {
        var opts = new FixedWidthReadOptions { SkipRows = 1, HasHeaderRow = true };
        // Skip 1 row, then header row, then 2 data rows
        var bytes = MakeUtf8("ignore-this\nheader-line\nname100001\nname200002\n");
        Assert.Equal(2, CountRecords(bytes, opts));
    }
}
