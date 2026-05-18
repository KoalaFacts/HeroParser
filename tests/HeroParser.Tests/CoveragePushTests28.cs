using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 28: FixedWidth column TryParseTimeZoneInfo/Byte/SByte/Enum/Equals.</summary>
public class CoveragePushTests28
{
    // ---------- FixedWidthCharSpanColumn TryParse remaining ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharColumn_TryParseTimeZoneInfo_Valid()
    {
        string line = "UTC\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 3);
        Assert.True(col.TryParseTimeZoneInfo(out var tz));
        Assert.Equal(TimeZoneInfo.Utc.Id, tz.Id);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharColumn_TryParseTimeZoneInfo_Empty_False()
    {
        string line = "    \n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        // Trim to empty (all spaces).
        var col = reader.Current.GetField(0, 4, ' ', FieldAlignment.Left);
        Assert.False(col.TryParseTimeZoneInfo(out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharColumn_TryParseTimeZoneInfo_NotFound_False()
    {
        string line = "FakeZone\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 8);
        Assert.False(col.TryParseTimeZoneInfo(out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharColumn_TryParseByte_TryParseSByte()
    {
        string line = "127-128\n"; // first 3 chars "127", next 4 chars "-128"
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var c1 = reader.Current.GetField(0, 3);
        Assert.True(c1.TryParseByte(out var b));
        Assert.Equal((byte)127, b);

        var c2 = reader.Current.GetField(3, 4);
        Assert.True(c2.TryParseSByte(out var sb));
        Assert.Equal((sbyte)-128, sb);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharColumn_TryParseEnum()
    {
        string line = "Red\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 3);
        Assert.True(col.TryParseEnum<Color>(out var c));
        Assert.Equal(Color.Red, c);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharColumn_TryParseEnum_BadValue_False()
    {
        string line = "Foo\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 3);
        Assert.False(col.TryParseEnum<Color>(out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharColumn_Equals_Comparisons()
    {
        string line = "Hello\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 5);
        Assert.True(col.Equals("Hello"));
        Assert.False(col.Equals("World"));
        Assert.False(col.Equals(null));
    }

    // ---------- FixedWidthByteSpanColumn equivalents ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteColumn_TryParseTimeZoneInfo_Valid()
    {
        byte[] bytes = "UTC\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 3);
        Assert.True(col.TryParseTimeZoneInfo(out var tz));
        Assert.Equal(TimeZoneInfo.Utc.Id, tz.Id);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteColumn_TryParseTimeZoneInfo_Empty_False()
    {
        byte[] bytes = "    \n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 4, (byte)' ', FieldAlignment.Left);
        Assert.False(col.TryParseTimeZoneInfo(out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteColumn_TryParseTimeZoneInfo_NotFound_False()
    {
        byte[] bytes = "FakeZone\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 8);
        Assert.False(col.TryParseTimeZoneInfo(out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteColumn_TryParseEnum()
    {
        byte[] bytes = "Red\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 3);
        Assert.True(col.TryParseEnum<Color>(out var c));
        Assert.Equal(Color.Red, c);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteColumn_Equals_Comparisons()
    {
        byte[] bytes = "Hello\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 5);
        Assert.True(col.Equals("Hello"));
        Assert.False(col.Equals("World"));
        Assert.False(col.Equals(null));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteColumn_TryParseDateTime_FailureCases()
    {
        byte[] bytes = "garbage\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 7);
        Assert.False(col.TryParseDateTime(out _));
    }

    // ---------- CountingReadStream not-yet-covered methods ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CountingReadStream_LeaveOpenTrue_Dispose_DoesNotCloseInner()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);
        // Read so we exercise the Read path.
        byte[] buf = new byte[3];
        int read = await stream.ReadAsync(buf.AsMemory(), TestContext.Current.CancellationToken);
        Assert.True(read > 0);
        await stream.DisposeAsync();
        Assert.True(inner.CanRead);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_PositionSetter()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));
        using var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true)
        { Position = 7 };
        Assert.Equal(7, stream.Position);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Seek_FromBeginCurrentEnd()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));
        using var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);
        Assert.Equal(5, stream.Seek(5, SeekOrigin.Begin));
        Assert.Equal(7, stream.Seek(2, SeekOrigin.Current));
        Assert.Equal(13, stream.Seek(0, SeekOrigin.End));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_CanProperties()
    {
        using var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello"));
        using var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);
        // Exercise CanRead / CanSeek / CanWrite getters.
        _ = stream.CanRead;
        _ = stream.CanSeek;
        _ = stream.CanWrite;
    }

    // ---------- CsvCharToByteBinderAdapter (used when reading char path with byte binder) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvCharToByteBinderAdapter_ExercisedViaCharReader()
    {
        // Csv.Read<T>().FromText() uses the adapter to bridge char data to byte binder.
        using var reader = Csv.Read<CoveragePerson>()
            .Map(p => p.Name, c => c.Name("Name"))
            .Map(p => p.Age, c => c.Name("Age"))
            .FromText("Name,Age\nAlice,30\nBob,25\nCarol,40\n");
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvCharToByteBinderAdapter_LargeFields()
    {
        // Force the adapter to handle long fields (rented byte buffer path).
        var sb = new StringBuilder("Name,Age\n");
        for (int i = 0; i < 100; i++)
        {
            sb.Append(new string('x', 5000)).Append(',').Append(i).Append('\n');
        }
        using var reader = Csv.Read<CoveragePerson>()
            .Map(p => p.Name, c => c.Name("Name"))
            .Map(p => p.Age, c => c.Name("Age"))
            .FromText(sb.ToString());
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Equal(100, n);
    }
}
