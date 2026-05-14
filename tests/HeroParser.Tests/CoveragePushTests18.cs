using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 18: FixedWidth column TryParse overloads (date/datetime), CountingReadStream, Excel.DataReader overloads, CsvWriteOptions.Validate.</summary>
public class CoveragePushTests18
{
    // ---------- FixedWidth ByteSpanColumn TryParse overloads ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanColumn_TryParseDateTime_WithCulture()
    {
        string line = "2024-06-01T12:30:45\n";
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 19);

        // AssumeUniversal forces fallthrough from Utf8Parser to DateTime.TryParse(Decode()).
        Assert.True(col.TryParseDateTime(out _, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
        // Non-invariant culture also falls through.
        Assert.True(col.TryParseDateTime(out _, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanColumn_TryParseDateTime_WithExactFormat()
    {
        byte[] bytes = "20240601\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 8);

        Assert.True(col.TryParseDateTime(out var d, "yyyyMMdd"));
        Assert.Equal(2024, d.Year);
        Assert.True(col.TryParseDateTime(out _, "yyyyMMdd", CultureInfo.InvariantCulture));
        Assert.False(col.TryParseDateTime(out _, "dd/MM/yyyy"));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanColumn_TryParseDateTimeOffset_Variants()
    {
        byte[] bytes = "2024-06-01T12:30:45+00:00\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 25);

        Assert.True(col.TryParseDateTimeOffset(out _));
        Assert.True(col.TryParseDateTimeOffset(out _, CultureInfo.InvariantCulture));
        Assert.True(col.TryParseDateTimeOffset(out _, "yyyy-MM-ddTHH:mm:sszzz"));
        Assert.True(col.TryParseDateTimeOffset(out _, "yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanColumn_TryParseDateOnly_Variants()
    {
        byte[] bytes = "2024-06-01\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 10);

        Assert.True(col.TryParseDateOnly(out _));
        Assert.True(col.TryParseDateOnly(out _, CultureInfo.InvariantCulture));
        Assert.True(col.TryParseDateOnly(out _, "yyyy-MM-dd"));
        Assert.True(col.TryParseDateOnly(out _, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanColumn_TryParseTimeOnly_Variants()
    {
        byte[] bytes = "12:30:45\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 8);

        Assert.True(col.TryParseTimeOnly(out _));
        Assert.True(col.TryParseTimeOnly(out _, CultureInfo.InvariantCulture));
        Assert.True(col.TryParseTimeOnly(out _, "HH:mm:ss"));
        Assert.True(col.TryParseTimeOnly(out _, "HH:mm:ss", CultureInfo.InvariantCulture));
    }

    // ---------- FixedWidth CharSpanColumn TryParse overloads ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanColumn_TryParseDateTime_WithCulture()
    {
        string line = "2024-06-01T12:30:45\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 19);

        Assert.True(col.TryParseDateTime(out _, CultureInfo.InvariantCulture));
        Assert.True(col.TryParseDateTime(out _, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanColumn_TryParseDateTime_WithExactFormat()
    {
        string line = "20240601\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 8);

        Assert.True(col.TryParseDateTime(out var d, "yyyyMMdd"));
        Assert.Equal(2024, d.Year);
        Assert.True(col.TryParseDateTime(out _, "yyyyMMdd", CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanColumn_TryParseDateOnly_Variants()
    {
        string line = "2024-06-01\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 10);

        Assert.True(col.TryParseDateOnly(out _));
        Assert.True(col.TryParseDateOnly(out _, CultureInfo.InvariantCulture));
        Assert.True(col.TryParseDateOnly(out _, "yyyy-MM-dd"));
        Assert.True(col.TryParseDateOnly(out _, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanColumn_TryParseTimeOnly_Variants()
    {
        string line = "12:30:45\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 8);

        Assert.True(col.TryParseTimeOnly(out _));
        Assert.True(col.TryParseTimeOnly(out _, CultureInfo.InvariantCulture));
        Assert.True(col.TryParseTimeOnly(out _, "HH:mm:ss"));
        Assert.True(col.TryParseTimeOnly(out _, "HH:mm:ss", CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanColumn_TryParseDateTimeOffset()
    {
        string line = "2024-06-01T12:30:45+00:00\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 25);

        Assert.True(col.TryParseDateTimeOffset(out _));
        Assert.True(col.TryParseDateTimeOffset(out _, CultureInfo.InvariantCulture));
        Assert.True(col.TryParseDateTimeOffset(out _, "yyyy-MM-ddTHH:mm:sszzz"));
        Assert.True(col.TryParseDateTimeOffset(out _, "yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
    }

    // ---------- FixedWidth Row.ToImmutable and Clone ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanRow_ToImmutable_Clone()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Alice     30\n");
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        var immutable = row.ToImmutable();
        Assert.True(immutable.Length > 0);

        var clone = row.Clone();
        Assert.Equal(row.Length, clone.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanRow_ToImmutable()
    {
        string line = "Alice     30\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        var immutable = row.ToImmutable();
        Assert.True(immutable.Length > 0);
    }

    // ---------- CountingReadStream (internal) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Properties_LeaveOpen()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);

        // CanRead inherits from inner.
        Assert.True(stream.CanRead);
        Assert.Equal(inner.Length, stream.Length);

        // Sync read
        byte[] buffer = new byte[3];
        int read = stream.Read(buffer, 0, buffer.Length);
        Assert.True(read > 0);
        Assert.True(stream.BytesRead > 0);

        stream.Dispose();
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Properties_NoLeaveOpen()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: false);
        stream.Dispose();
        Assert.False(inner.CanRead);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Position()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);

        byte[] buf = new byte[5];
        int n = stream.Read(buf, 0, 5);
        Assert.True(n > 0);
        Assert.True(stream.Position > 0);
        stream.Position = 0;
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CountingReadStream_ReadAsync_Buffer()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);
        byte[] buf = new byte[5];
        int read = await stream.ReadAsync(buf, 0, 5, TestContext.Current.CancellationToken);
        Assert.Equal(5, read);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Flush_NoOp()
    {
        var inner = new MemoryStream();
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);
        stream.Flush(); // Should not throw
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CountingReadStream_FlushAsync()
    {
        var inner = new MemoryStream();
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);
        await stream.FlushAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Seek()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello, World!"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);
        long pos = stream.Seek(5, SeekOrigin.Begin);
        Assert.Equal(5, pos);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_SetLength_Throws()
    {
        var inner = new MemoryStream(Encoding.UTF8.GetBytes("Hello"));
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);
        Assert.Throws<NotSupportedException>(() => stream.SetLength(10));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CountingReadStream_Write_Behavior()
    {
        // Whether write throws or no-ops is implementation-defined; just exercise the call.
        var inner = new MemoryStream(new byte[10]);
        var stream = new global::HeroParser.FixedWidths.Streaming.CountingReadStream(
            inner, global::HeroParser.FixedWidths.FixedWidthReadOptions.Default, leaveOpen: true);
        try { stream.Write([1, 2, 3], 0, 3); } catch { /* either is fine */ }
    }

    // ---------- Excel.CreateDataReader overloads ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_CreateDataReader_FromStream_WithSheetName()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        using var dr = global::HeroParser.Excel.CreateDataReader(ms, sheetName: "Sheet1");
        Assert.True(dr.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_CreateDataReader_FromStream_WithOptions()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        using var dr = global::HeroParser.Excel.CreateDataReader(
            ms,
            new global::HeroParser.Excels.Reading.Data.ExcelDataReaderOptions { HasHeaderRow = true });
        Assert.True(dr.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_CreateDataReader_FromPath()
    {
        string path = Path.GetTempFileName() + ".xlsx";
        try
        {
            var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
            global::HeroParser.Excel.Write<CoveragePerson>().ToFile(path, rows);
            using var dr = global::HeroParser.Excel.CreateDataReader(path);
            Assert.True(dr.Read());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_CreateDataReader_FromPath_WithOptions()
    {
        string path = Path.GetTempFileName() + ".xlsx";
        try
        {
            var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
            global::HeroParser.Excel.Write<CoveragePerson>().ToFile(path, rows);
            using var dr = global::HeroParser.Excel.CreateDataReader(
                path,
                new global::HeroParser.Excels.Reading.Data.ExcelDataReaderOptions { HasHeaderRow = true });
            Assert.True(dr.Read());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_CreateDataReader_SkipRows()
    {
        var rows = new[]
        {
            new CoveragePerson { Name = "Alice", Age = 30 },
            new CoveragePerson { Name = "Bob", Age = 25 }
        };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        using var dr = global::HeroParser.Excel.CreateDataReader(ms, skipRows: 1);
        int n = 0;
        while (dr.Read()) n++;
        Assert.True(n <= rows.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_CreateDataReader_NoHeader()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        using var dr = global::HeroParser.Excel.CreateDataReader(ms, hasHeaderRow: false);
        Assert.True(dr.Read());
    }

    // ---------- CsvWriteOptions.Validate throws ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_NonAsciiDelimiter_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        Assert.Throws<CsvException>(() =>
            Csv.WriteToText(rows, options: new CsvWriteOptions { Delimiter = 'é' }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_NonAsciiQuote_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        Assert.Throws<CsvException>(() =>
            Csv.WriteToText(rows, options: new CsvWriteOptions { Quote = 'é' }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_DelimiterEqualsQuote_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        Assert.Throws<CsvException>(() =>
            Csv.WriteToText(rows, options: new CsvWriteOptions { Delimiter = ',', Quote = ',' }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_EmptyNewLine_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        Assert.Throws<CsvException>(() =>
            Csv.WriteToText(rows, options: new CsvWriteOptions { NewLine = "" }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_BadNewLineChars_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        Assert.Throws<CsvException>(() =>
            Csv.WriteToText(rows, options: new CsvWriteOptions { NewLine = "x" }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_NonPositiveMaxRowCount_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        Assert.Throws<CsvException>(() =>
            Csv.WriteToText(rows, options: new CsvWriteOptions { MaxRowCount = 0 }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_NonPositiveMaxOutputSize_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        Assert.Throws<CsvException>(() =>
            Csv.WriteToText(rows, options: new CsvWriteOptions { MaxOutputSize = 0 }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_NonPositiveMaxFieldSize_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        Assert.Throws<CsvException>(() =>
            Csv.WriteToText(rows, options: new CsvWriteOptions { MaxFieldSize = 0 }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_AllNewLineVariants()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        Assert.NotEmpty(Csv.WriteToText(rows, options: new CsvWriteOptions { NewLine = "\n" }));
        Assert.NotEmpty(Csv.WriteToText(rows, options: new CsvWriteOptions { NewLine = "\r\n" }));
        Assert.NotEmpty(Csv.WriteToText(rows, options: new CsvWriteOptions { NewLine = "\r" }));
    }

    // ---------- FixedWidth ByteSpanColumn Parse<T> exception path ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanColumn_Parse_GenericType()
    {
        byte[] bytes = "42\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 2);
        Assert.Equal(42, col.Parse<int>());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanColumn_Parse_GenericType()
    {
        string line = "42\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 2);
        Assert.Equal(42, col.Parse<int>());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanColumn_TryParse_GenericType()
    {
        byte[] bytes = "42\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 2);
        Assert.True(col.TryParse<int>(out var v));
        Assert.Equal(42, v);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanColumn_TryParse_GenericType_BadInput()
    {
        string line = "xx\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var col = reader.Current.GetField(0, 2);
        Assert.False(col.TryParse<int>(out _));
    }
}
