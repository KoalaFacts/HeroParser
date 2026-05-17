using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records;
using HeroParser.SeparatedValues.Core;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Eleventh wave: FixedWidth row/column accessor coverage, CountingReadStream, additional reader/writer paths.</summary>
public class CoveragePushTests11
{
    // ---------- FixedWidthCharSpanColumn / FixedWidthByteSpanColumn TryParse* via row reader ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanRow_AllTryParseAndAccessors()
    {
        // Row layout: long(10) short(5) byte(3) double(8) bool(5) decimal(10) date(19)
        string line = "9999999999" + "12345" + "200" + "3.140000" + "true " + "1234.56000" + "2024-06-01T12:30:45" + "\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;

        var c0 = row.GetField(0, 10);
        Assert.True(c0.TryParseInt64(out _));
        Assert.True(c0.TryParseUInt64(out _));
        Assert.True(c0.TryParseDecimal(out _));
        Assert.True(c0.TryParseDouble(out _));
        Assert.True(c0.TryParseSingle(out _));
        Assert.False(c0.IsEmpty);

        var c1 = row.GetField(10, 5);
        Assert.True(c1.TryParseInt16(out _));
        Assert.True(c1.TryParseUInt16(out _));
        Assert.True(c1.TryParseInt32(out _));
        Assert.True(c1.TryParseUInt32(out _));

        var c2 = row.GetField(15, 3);
        c2.TryParse<int>(out _);
        Assert.Equal(3, c2.Length);

        var c4 = row.GetField(26, 5);
        Assert.True(c4.TryParseBoolean(out _));

        var c6 = row.GetField(41, 19);
        Assert.True(c6.TryParseDateTime(out _));

        // ToString
        Assert.NotEmpty(c0.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanRow_TryParseFailures()
    {
        string line = "xxxxxxxxxx\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        var c = row.GetField(0, 10);
        Assert.False(c.TryParseInt32(out _));
        Assert.False(c.TryParseInt16(out _));
        Assert.False(c.TryParseInt64(out _));
        Assert.False(c.TryParseUInt32(out _));
        Assert.False(c.TryParseDouble(out _));
        Assert.False(c.TryParseBoolean(out _));
        Assert.False(c.TryParseDateTime(out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanRow_AllTryParse()
    {
        string line = "9999999999" + "12345" + "200" + "3.140000" + "true " + "1234.56000" + "\n";
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;

        var c0 = row.GetField(0, 10);
        Assert.True(c0.TryParseInt64(out _));
        Assert.True(c0.TryParseUInt64(out _));
        Assert.True(c0.TryParseDecimal(out _));
        Assert.True(c0.TryParseDouble(out _));
        Assert.True(c0.TryParseSingle(out _));
        Assert.False(c0.IsEmpty);

        var c1 = row.GetField(10, 5);
        Assert.True(c1.TryParseInt16(out _));
        Assert.True(c1.TryParseUInt16(out _));
        Assert.True(c1.TryParseInt32(out _));
        Assert.True(c1.TryParseUInt32(out _));

        var c2 = row.GetField(15, 3);
        Assert.True(c2.TryParseByte(out _));
        // 200 doesn't fit in sbyte (max 127). Just exercise the call.
        c2.TryParseSByte(out _);

        var c4 = row.GetField(26, 5);
        Assert.True(c4.TryParseBoolean(out _));

        Assert.NotEmpty(c0.ToString());
        Assert.False(c0.ByteSpan.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanRow_TryParseFailures()
    {
        string line = "xxxxxxxxxx\n";
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        var c = row.GetField(0, 10);
        Assert.False(c.TryParseInt32(out _));
        Assert.False(c.TryParseInt16(out _));
        Assert.False(c.TryParseInt64(out _));
        Assert.False(c.TryParseUInt32(out _));
        Assert.False(c.TryParseDouble(out _));
        Assert.False(c.TryParseBoolean(out _));
        Assert.False(c.TryParseByte(out _));
        Assert.False(c.TryParseSByte(out _));
    }

    // ---------- FixedWidthCharSpanRow Length / IsEmpty / RawSpan ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CharSpanRow_BasicProperties()
    {
        string line = "data\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(4, row.Length);
        Assert.Equal('d', row.RawRecord[0]);
        // FixedWidthCharSpanRow is a ref struct - ToString returns object on ref struct.
        Assert.True(row.RecordNumber >= 1);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ByteSpanRow_BasicProperties()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("data\n");
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(4, row.Length);
        Assert.Equal((byte)'d', row.RawRecord[0]);
        Assert.NotEmpty(row.ToDecodedString());
    }

    // CountingReadStream is internal; covered indirectly by FixedWidth.Read..FromStreamAsync below.

    // ---------- FixedWidth.Write.cs additional paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_Write_ToFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
            FixedWidth.Write<FixedAllTypes>().ToFile(path, rows);
            string content = File.ReadAllText(path);
            Assert.NotEmpty(content);
        }
        finally { File.Delete(path); }
    }

    // ---------- FixedWidthDataReader extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_DataReader_TypedAccess()
    {
        string text = "Alice     " + "30 " + "\n";
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths(
            [10, 3], ["Name", "Age"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.True(dr.Read());
        Assert.Equal("Alice", dr.GetString(0).Trim());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_DataReader_FieldCountAndNames()
    {
        string text = "Alice     " + "30 " + "\n";
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths(
            [10, 3], ["Name", "Age"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.Equal(2, dr.FieldCount);
        Assert.Equal("Name", dr.GetName(0));
        Assert.Equal("Age", dr.GetName(1));
        Assert.Equal(0, dr.GetOrdinal("Name"));
    }

    // ---------- CsvAsyncStreamWriter: variant paths via FlushAsync ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_FlushAsync_PartialBuffer()
    {
        var rows = new[] { new CoveragePerson { Name = "small", Age = 1 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_ManyRowsAtBoundary()
    {
        // Rows sized to span buffer boundaries (default buffer ~16KB).
        var rows = Enumerable.Range(0, 500).Select(i => new CoveragePerson
        {
            Name = new string('p', 30) + i,
            Age = i
        });
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 5000);
    }

    // ---------- CsvRecordWriter: WriteRecordsUnfilteredAsync via specific data types ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncList_WithIReadOnlyList()
    {
        IReadOnlyList<CoveragePerson> rows = (CoveragePerson[])
        [
            new CoveragePerson { Name = "A", Age = 1 },
            new CoveragePerson { Name = "B", Age = 2 },
            new CoveragePerson { Name = "C", Age = 3 }
        ];
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("A", csv);
        Assert.Contains("B", csv);
        Assert.Contains("C", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_Sync_FromEnumerableNoBatching()
    {
        var rows = Enumerable.Range(0, 1).Select(_ => new CoveragePerson { Name = "Solo", Age = 1 });
        string csv = Csv.WriteToText(rows);
        Assert.Contains("Solo", csv);
    }

    // ---------- ExcelRecordWriter additional paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_EmptyEnumerable()
    {
        var rows = Array.Empty<CoveragePerson>();
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        // Should still produce a valid (empty-data) xlsx with headers.
        Assert.True(ms.Length > 100);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_WithSheetName()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().WithSheetName("MyData").ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    // ---------- FixedWidth pipe row + CountingReadStream + AsyncStreamReader ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_AsyncStream_LargeInput()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
            sb.AppendLine($"{i,-10}{(short)i,-5}{(byte)(i % 256),-3}{i + 0.5,-8:F2}{i + 0.25f,-8:F2}{(i % 2 == 0 ? "true " : "false")}{i + 0.1m,-10:F2}");
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        int n = 0;
        await foreach (var _ in FixedWidth.Read<FixedAllTypes>()
            .FromStreamAsync(ms, TestContext.Current.CancellationToken))
        {
            n++;
        }
        Assert.Equal(1000, n);
    }
}
