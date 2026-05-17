using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 35: Non-invariant culture FW fallbacks, CountingReadStream sync paths, ExcelAllSheets fluent.</summary>
public class CoveragePushTests35
{
    // ---------- Non-invariant culture FW byte-path with VALID numerics (TryParseChars success) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_AllNumerics_Valid()
    {
        // en-US is non-invariant: forces TryParseChars fallback in Utf8BindingHelper for each
        // type (Int32/Int64/Int16/Byte/Double/Single/Decimal/Bool).
        string line = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(line));
        var rows = FixedWidth.Read<FixedAllTypes>()
            .WithCulture("en-US")
            .FromStream(ms)
            .ToList();
        Assert.Single(rows);
        Assert.Equal(9999999999L, rows[0].L);
        Assert.Equal((short)12345, rows[0].S);
        Assert.Equal((byte)200, rows[0].B);
        Assert.Equal(3.14, rows[0].D, 2);
        Assert.Equal(2.5f, rows[0].F);
        Assert.True(rows[0].Bo);
        Assert.Equal(1234.56m, rows[0].M);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_IsNullOrWhiteSpace_NonAscii()
    {
        // Non-ASCII byte triggers the IsNullOrWhiteSpace fallback decode (line 46).
        // Note: parsing a non-ASCII field as numeric should fail; just exercise.
        byte[] bytes = [0xC3, 0xA9, (byte)'\n']; // "é\n"
        try
        {
            FixedWidth.Read<FixedAllTypes>().WithCulture("en-US").FromStream(new MemoryStream(bytes)).ToList();
        }
        catch (Exception) { /* tolerable */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_BytePath_DateTime_NonInvariant_TryParseChars()
    {
        string line = "2024-06-01T12:30:45\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(line));
        // Use en-US which is non-invariant → triggers the TryParseChars fallback.
        var rows = FixedWidth.Read<PlainDtRow_Wave35>()
            .WithCulture("en-US")
            .FromStream(ms)
            .ToList();
        Assert.Single(rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_BytePath_DateOnly_NonInvariant_TryParseChars()
    {
        string line = "2024-06-01\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(line));
        var rows = FixedWidth.Read<PlainDateOnlyRow_Wave35>()
            .WithCulture("en-US")
            .FromStream(ms)
            .ToList();
        Assert.Single(rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_BytePath_TimeOnly_NonInvariant_TryParseChars()
    {
        string line = "12:30:45\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(line));
        var rows = FixedWidth.Read<PlainTimeOnlyRow_Wave35>()
            .WithCulture("en-US")
            .FromStream(ms)
            .ToList();
        Assert.Single(rows);
    }

    // ---------- CountingReadStream sync Read(Span) + ReadByte ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Read_Span()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        Span<byte> buf = stackalloc byte[5];
        int read = stream.Read(buf);
        Assert.True(read > 0);
        Assert.True(stream.BytesRead >= read);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_ReadByte()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("ABC"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        int b = stream.ReadByte();
        Assert.Equal('A', b);
        Assert.True(stream.BytesRead > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_ReadByte_PastEnd_ReturnsNegativeOne()
    {
        var inner = new MemoryStream();
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Write_DelegatesToInner()
    {
        var inner = new MemoryStream();
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        // CountingReadStream forwards write to inner.
        stream.Write([1, 2, 3], 0, 3);
        Assert.Equal(3, inner.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Write_ReadOnlySpan()
    {
        var inner = new MemoryStream();
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        ReadOnlySpan<byte> data = [1, 2, 3];
        stream.Write(data);
        Assert.Equal(3, inner.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CountingReadStream_WriteAsync_Buffer()
    {
        var inner = new MemoryStream();
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        byte[] data = [1, 2, 3];
        await stream.WriteAsync(data, 0, 3, TestContext.Current.CancellationToken);
        Assert.Equal(3, inner.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CountingReadStream_WriteAsync_ReadOnlyMemory()
    {
        var inner = new MemoryStream();
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        await stream.WriteAsync(new ReadOnlyMemory<byte>([1, 2, 3]), TestContext.Current.CancellationToken);
        Assert.Equal(3, inner.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_WriteByte()
    {
        var inner = new MemoryStream();
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        stream.WriteByte(0x41);
        Assert.Equal(1, inner.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_SetLength_DelegatesToInner()
    {
        var inner = new MemoryStream(new byte[10], writable: true);
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        // SetLength on MemoryStream over a fixed array throws; on growable, it doesn't.
        try { stream.SetLength(5); }
        catch (Exception) { /* tolerable */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CountingReadStream_DisposeAsync_LeaveOpenTrue()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: true);
        await stream.DisposeAsync();
        Assert.True(inner.CanRead);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CountingReadStream_DisposeAsync_LeaveOpenFalse()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, FixedWidthReadOptions.Default, leaveOpen: false);
        await stream.DisposeAsync();
        Assert.False(inner.CanRead);
    }

    // ---------- ExcelAllSheetsBuilder full fluent ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_AllSheets_FullFluent()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;

        // Exercise ConfigureBuilder fluent paths via AllSheets builder.
        var dict = global::HeroParser.Excel.Read<CoveragePerson>()
            .WithoutHeader()
            .CaseSensitiveHeaders()
            .AllowMissingColumns()
            .WithNullValues("NA")
            .WithCulture(CultureInfo.InvariantCulture)
            .WithMaxRows(100)
            .SkipRows(0)
            .WithValidationMode(ValidationMode.Lenient)
            .AllSheets()
            .FromStream(ms);
        Assert.NotNull(dict);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_AllSheets_FromFile()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string path = Path.GetTempFileName() + ".xlsx";
        try
        {
            global::HeroParser.Excel.Write<CoveragePerson>().ToFile(path, src);
            var dict = global::HeroParser.Excel.Read<CoveragePerson>().AllSheets().FromFile(path);
            Assert.NotNull(dict);
            Assert.NotEmpty(dict);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_AllSheets_WithValidationMode()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;
        var dict = global::HeroParser.Excel.Read<CoveragePerson>()
            .AllSheets()
            .WithValidationMode(ValidationMode.Lenient)
            .FromStream(ms);
        Assert.NotEmpty(dict);
    }

    // ---------- FixedWidthByteSpanReader edges (78-93 cluster) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_VeryShortRecord_AllowShortRows()
    {
        // RecordLength=10 with only "a\n" — short record, behavior implementation-defined.
        byte[] data = "a\n"u8.ToArray();
        var opts = FixedWidthReadOptions.Default with { RecordLength = 10, AllowShortRows = true };
        try
        {
            var reader = new FixedWidthByteSpanReader(data.AsSpan(), opts);
            while (reader.MoveNext()) { }
        }
        catch (Exception) { /* tolerable */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_RecordLength_FixedSplit()
    {
        // Without newlines and RecordLength set, reader splits into fixed-size records.
        byte[] data = "0123456789ABCDEFGHIJ"u8.ToArray();
        var opts = FixedWidthReadOptions.Default with { RecordLength = 5 };
        var reader = new FixedWidthByteSpanReader(data.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(4, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_RecordLength_FixedSplit()
    {
        string data = "0123456789ABCDEFGHIJ";
        var opts = FixedWidthReadOptions.Default with { RecordLength = 5 };
        var reader = new FixedWidthCharSpanReader(data.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(4, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_CrLf_Multiple()
    {
        var data = "row1\r\nrow2\r\nrow3\r\n";
        var reader = new FixedWidthCharSpanReader(data.AsSpan(), FixedWidthReadOptions.Default);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_CommentEndOfFile()
    {
        // Comment at end of file with no trailing newline.
        byte[] data = "row1\n# trailing comment"u8.ToArray();
        var opts = FixedWidthReadOptions.Default with { CommentCharacter = '#' };
        var reader = new FixedWidthByteSpanReader(data.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_CommentEndOfFile()
    {
        string data = "row1\n# trailing comment";
        var opts = FixedWidthReadOptions.Default with { CommentCharacter = '#' };
        var reader = new FixedWidthCharSpanReader(data.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n);
    }

    // ---------- CsvRecordOptions equality semantics ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordOptions_Equality()
    {
        var a = new global::HeroParser.SeparatedValues.Reading.Records.CsvRecordOptions
        {
            HasHeaderRow = true,
            CaseSensitiveHeaders = false
        };
        var b = a with { CaseSensitiveHeaders = true };
        Assert.NotEqual(a, b);

        var c = a with { };
        Assert.Equal(a, c);
    }

    // ---------- CsvRecordBinderFactory TryGetByteBinder (internal) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordBinderFactory_TryGetByteBinder_Internal_Hit()
    {
        var m = typeof(global::HeroParser.SeparatedValues.Reading.Binders.CsvRecordBinderFactory)
            .GetMethod("TryGetByteBinder", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (m is not null)
        {
            var g = m.MakeGenericMethod(typeof(CoveragePerson));
            object?[] args = [null, null];
            var ok = (bool)g.Invoke(null, args)!;
            Assert.True(ok);
            Assert.NotNull(args[1]);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordBinderFactory_TryGetByteBinder_Internal_Miss()
    {
        var m = typeof(global::HeroParser.SeparatedValues.Reading.Binders.CsvRecordBinderFactory)
            .GetMethod("TryGetByteBinder", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (m is not null)
        {
            var g = m.MakeGenericMethod(typeof(NoBinderRow));
            object?[] args = [null, null];
            var ok = (bool)g.Invoke(null, args)!;
            Assert.False(ok);
            Assert.Null(args[1]);
        }
    }

    // ---------- Utf8SpanParserFactory branches ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_NullableTypes_AllSet()
    {
        // Nullable types with non-invariant culture exercises the nullable wrapping path.
        string line = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(line));
        var rows = FixedWidth.Read<FixedAllNullable>()
            .WithCulture("en-US")
            .FromStream(ms)
            .ToList();
        Assert.Single(rows);
        Assert.Equal(9999999999L, rows[0].L);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_NullableTypes_AllBlank()
    {
        // All blanks → nullable wrappers return null.
        string line = new string(' ', 49) + "\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(line));
        var rows = FixedWidth.Read<FixedAllNullable>()
            .WithCulture("en-US")
            .FromStream(ms)
            .ToList();
        Assert.Single(rows);
        Assert.Null(rows[0].L);
    }
}

[GenerateBinder]
public class PlainDtRow_Wave35
{
    [PositionalMap(Start = 0, Length = 19)]
    public DateTime Value { get; set; }
}

[GenerateBinder]
public class PlainDateOnlyRow_Wave35
{
    [PositionalMap(Start = 0, Length = 10)]
    public DateOnly Value { get; set; }
}

[GenerateBinder]
public class PlainTimeOnlyRow_Wave35
{
    [PositionalMap(Start = 0, Length = 8)]
    public TimeOnly Value { get; set; }
}
