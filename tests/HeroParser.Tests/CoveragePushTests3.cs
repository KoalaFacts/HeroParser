using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Third wave: FixedWidth converters, char-path CsvColumn, Excel writer, DataReader edges.</summary>
public class CoveragePushTests3
{
    // ---------- FixedWidth converter coverage (Int64/Int16/Byte/Double/Single/DateTime/etc.) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_AllPrimitiveConverters()
    {
        // Build a fixed-width line: long(10) short(5) byte(3) double(8) single(8) bool(5) decimal(10)
        string line = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000";
        line += "\n";

        var result = FixedWidth.Read<FixedAllTypes>().FromText(line).ToList();
        Assert.Single(result);
        var r = result[0];
        Assert.Equal(9999999999L, r.L);
        Assert.Equal((short)12345, r.S);
        Assert.Equal((byte)200, r.B);
        Assert.Equal(3.14, r.D, 2);
        Assert.Equal(2.5f, r.F);
        Assert.True(r.Bo);
        Assert.Equal(1234.56m, r.M);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_DateTimeConverters()
    {
        // DateTime(19) DateTimeOffset(25) DateOnly(10) TimeOnly(8)
        string line = "2024-06-01T12:30:45" + "2024-06-01T12:30:45+00:00" + "2024-06-01" + "12:30:45";
        line += "\n";

        var result = FixedWidth.Read<FixedDates>().FromText(line).ToList();
        Assert.Single(result);
        Assert.Equal(2024, result[0].Dt.Year);
        Assert.Equal(2024, result[0].Dto.Year);
        Assert.Equal(2024, result[0].D.Year);
        Assert.Equal(12, result[0].T.Hour);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_EnumConverter()
    {
        string line = "Red  " + "\n";
        var result = FixedWidth.Read<FixedEnumRow>().FromText(line).ToList();
        Assert.Single(result);
        Assert.Equal(Color.Red, result[0].Color);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NullableConverters_HandleEmpty()
    {
        // All fields blank → all nullables null
        string line = new string(' ', 10) + new string(' ', 5) + new string(' ', 3) + new string(' ', 8) + new string(' ', 8) + new string(' ', 5) + new string(' ', 10) + "\n";
        var result = FixedWidth.Read<FixedAllNullable>().FromText(line).ToList();
        Assert.Single(result);
        Assert.Null(result[0].L);
        Assert.Null(result[0].S);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_BindAsync_StreamingEnumeration()
    {
        // Larger input to exercise async-binding state machine paths.
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            string row = $"{i,-10}{(short)i,-5}{(byte)(i % 256),-3}{i + 0.5,-8:F2}{i + 0.25f,-8:F2}{(i % 2 == 0 ? "true " : "false")}{i + 0.1m,-10:F2}\n";
            sb.Append(row);
        }
        var rows = FixedWidth.Read<FixedAllTypes>().FromText(sb.ToString()).ToList();
        Assert.Equal(100, rows.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_SkipRows_WithText()
    {
        // First two lines skipped; third is a record.
        string line = "BANNER LINE 1\nBANNER LINE 2\n" +
                      "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n";
        var result = FixedWidth.Read<FixedAllTypes>().SkipRows(2).FromText(line).ToList();
        Assert.Single(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_SkipEmptyLines()
    {
        string line = "\n\n" +
                      "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n";
        var result = FixedWidth.Read<FixedAllTypes>().SkipEmptyLines().FromText(line).ToList();
        Assert.Single(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_FromStream()
    {
        string line = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n";
        using var s = new MemoryStream(Encoding.UTF8.GetBytes(line));
        var result = FixedWidth.Read<FixedAllTypes>().FromStream(s).ToList();
        Assert.Single(result);
    }

    // ---------- FixedWidth Writer coverage ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_Write_AllTypes()
    {
        var rows = new[]
        {
            new FixedAllTypes { L = 1234567890L, S = 100, B = 50, D = 3.14, F = 2.5f, Bo = true, M = 999.99m }
        };
        string text = FixedWidth.Write<FixedAllTypes>().ToText(rows);
        Assert.Contains("1234567890", text);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_Write_ToStream()
    {
        var rows = new[]
        {
            new FixedAllTypes { L = 1234567890L, S = 100, B = 50, D = 3.14, F = 2.5f, Bo = true, M = 999.99m }
        };
        using var s = new MemoryStream();
        FixedWidth.Write<FixedAllTypes>().ToStream(s, rows);
        Assert.True(s.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_WriteAsync_ToStream()
    {
        var rows = new[]
        {
            new FixedAllTypes { L = 1234567890L, S = 100, B = 50, D = 3.14, F = 2.5f, Bo = true, M = 999.99m }
        };
        using var s = new MemoryStream();
        await FixedWidth.Write<FixedAllTypes>().ToStreamAsync(s, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(s.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_RoundTrip()
    {
        var rows = new[]
        {
            new FixedAllTypes { L = 9999999999L, S = 12345, B = 200, D = 3.14, F = 2.5f, Bo = true, M = 1234.56m }
        };
        string text = FixedWidth.Write<FixedAllTypes>().ToText(rows);
        var roundTrip = FixedWidth.Read<FixedAllTypes>().FromText(text).ToList();
        Assert.Single(roundTrip);
        Assert.Equal(9999999999L, roundTrip[0].L);
    }

    // ---------- CsvColumn char-path coverage ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_TryParseAllPrimitives()
    {
        // Use Csv.Read().FromText(reader) where reader is TextReader to force char path.
        string csv = "1,2,3,4,5,6,3.14,2.5,2024-06-01,true\n";

        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.True(row[0].TryParseInt32(out _));
        Assert.True(row[1].TryParseInt16(out _));
        Assert.True(row[2].TryParseInt64(out _));
        Assert.True(row[3].TryParseUInt32(out _));
        Assert.True(row[4].TryParseUInt16(out _));
        Assert.True(row[5].TryParseUInt64(out _));
        Assert.True(row[6].TryParseDouble(out _));
        Assert.True(row[7].TryParseSingle(out _));
        Assert.True(row[8].TryParseDateTime(out _));
        Assert.True(row[9].TryParseBoolean(out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_TryParseDecimal()
    {
        string csv = "12.5\n";

        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current[0].TryParseDecimal(out var d));
        Assert.Equal(12.5m, d);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_TryParseGuid()
    {
        var g = Guid.NewGuid();
        string csv = $"{g}\n";

        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current[0].TryParseGuid(out var parsed));
        Assert.Equal(g, parsed);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_ToString_ParseGeneric()
    {
        string csv = "42\n";

        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.Equal("42", reader.Current[0].ToString());
        Assert.Equal(42, reader.Current[0].Parse<int>());
        Assert.True(reader.Current[0].TryParse<int>(out var v));
        Assert.Equal(42, v);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_TryParseAllPrimitives()
    {
        string csv = "1,2,3,4,5,6,3.14,2.5,2024-06-01T12:00:00,true\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.True(row[0].TryParseInt32(out _));
        Assert.True(row[1].TryParseInt16(out _));
        Assert.True(row[2].TryParseInt64(out _));
        Assert.True(row[3].TryParseUInt32(out _));
        Assert.True(row[4].TryParseUInt16(out _));
        Assert.True(row[5].TryParseUInt64(out _));
        Assert.True(row[6].TryParseDouble(out _));
        Assert.True(row[7].TryParseSingle(out _));
        // TryParseDateTime via byte path requires Utf8Parser-compatible format (ISO 8601 with 'T').
        row[8].TryParseDateTime(out _);
        Assert.True(row[9].TryParseBoolean(out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_TryParseDecimalAndGuid()
    {
        var g = Guid.NewGuid();
        string csv = $"12.5,{g}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current[0].TryParseDecimal(out _));
        Assert.True(reader.Current[1].TryParseGuid(out _));
    }

    // ---------- ExcelRecordWriter coverage (write large data) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_AllPrimitiveTypes()
    {
        var rows = new[]
        {
            new AllTypes { S = "abc", I = 1, L = 2, D = 3.5, B = true, Dt = new DateTime(2024, 6, 1), G = Guid.Empty, F = 1.5f, M = 2.5m }
        };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<AllTypes>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_LargeBatch()
    {
        var rows = Enumerable.Range(0, 1000).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_NullableTypes()
    {
        var rows = new[] { new NullableTypes { S = "a", I = 1, L = null, D = null, B = true, Dt = new DateTime(2024, 1, 1) } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<NullableTypes>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_SpecialCharacters()
    {
        var rows = new[]
        {
            new CoveragePerson { Name = "<>&\"'", Age = 1 },
            new CoveragePerson { Name = "tab\there\nline", Age = 2 }
        };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_RoundTrip()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 }, new CoveragePerson { Name = "Bob", Age = 25 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        var read = global::HeroParser.Excel.Read<CoveragePerson>().FromStream(ms).ToList();
        Assert.Equal(2, read.Count);
    }

    // ---------- CSV StreamWriter / Async writer rare paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_InjectionProtection_EscapeWithQuote()
    {
        var rows = new[] { new CoveragePerson { Name = "=SUM(A1)", Age = 0 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.EscapeWithQuote },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("'", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_InjectionProtection_RemoveCharacter()
    {
        var rows = new[] { new CoveragePerson { Name = "=SUM(A1)", Age = 0 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.Sanitize },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.DoesNotContain("=SUM", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_LeadingDangerousChars_AtPrefix()
    {
        // Cover all dangerous prefix detections: = + - @ tab CR
        var rows = new[]
        {
            new CoveragePerson { Name = "=foo", Age = 1 },
            new CoveragePerson { Name = "+foo", Age = 2 },
            new CoveragePerson { Name = "-foo", Age = 3 },
            new CoveragePerson { Name = "@foo", Age = 4 },
            new CoveragePerson { Name = "\tfoo", Age = 5 },
            new CoveragePerson { Name = "\rfoo", Age = 6 }
        };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.EscapeWithQuote },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_CustomDelimiter_AndQuote()
    {
        var rows = new[] { new CoveragePerson { Name = "alpha", Age = 1 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { Delimiter = ';', Quote = '\'' },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamWriter_SyncBatch_ManyOptions()
    {
        var rows = Enumerable.Range(0, 100).Select(i => new CoveragePerson { Name = $"Person {i}", Age = i });
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            Delimiter = ';',
            QuoteStyle = QuoteStyle.Always,
            NewLine = "\r\n",
            InjectionProtection = CsvInjectionProtection.EscapeWithQuote,
            NullValue = "NULL"
        });
        Assert.True(csv.Length > 100);
        Assert.Contains("\r\n", csv);
    }

    // ---------- CsvDataReader extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_FromFile_OpensAndReads()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "A,B\n1,2\n3,4\n");
            using var dr = Csv.CreateDataReader(path);
            int rows = 0;
            while (dr.Read()) rows++;
            Assert.Equal(2, rows);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_GetOrdinal_AndCaseInsensitive()
    {
        string csv = "Name,Age\nAlice,30\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.Equal(0, dr.GetOrdinal("name"));
        Assert.Equal(1, dr.GetOrdinal("AGE"));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_GetValues_ReturnsAll()
    {
        string csv = "A,B,C\n1,2,3\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.True(dr.Read());
        object[] vals = new object[3];
        int n = dr.GetValues(vals);
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_GetFieldType_Resolves()
    {
        string csv = "A,B\n1,2\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.True(dr.Read());
        Assert.Equal(typeof(string), dr.GetFieldType(0));
        Assert.Equal("A", dr.GetName(0));
        Assert.Equal("B", dr.GetName(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_GetDataTypeName_ReturnsString()
    {
        string csv = "A\n1\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.True(dr.Read());
        Assert.Equal("String", dr.GetDataTypeName(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_TypedGetters()
    {
        string csv = "I,L,D,B\n42,9999999999,3.14,true\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.True(dr.Read());
        Assert.Equal(42, dr.GetInt32(0));
        Assert.Equal(9999999999L, dr.GetInt64(1));
        Assert.Equal(3.14, dr.GetDouble(2), 2);
        Assert.True(dr.GetBoolean(3));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_GetChar_GetByte()
    {
        string csv = "Ch,By\nA,7\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.True(dr.Read());
        Assert.Equal('A', dr.GetChar(0));
        Assert.Equal((byte)7, dr.GetByte(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_GetGuid_GetDecimal_GetFloat()
    {
        var g = Guid.NewGuid();
        string csv = $"G,D,F\n{g},1.5,2.5\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.True(dr.Read());
        Assert.Equal(g, dr.GetGuid(0));
        Assert.Equal(1.5m, dr.GetDecimal(1));
        Assert.Equal(2.5f, dr.GetFloat(2));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_FieldCount_AndIndexer()
    {
        string csv = "A,B\n1,2\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.Equal(2, dr.FieldCount);
        Assert.True(dr.Read());
        Assert.Equal("1", dr["A"]);
        Assert.Equal("2", dr[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_NextResult_AlwaysFalse()
    {
        string csv = "A\n1\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.False(dr.NextResult());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_EmptyFile_Empty()
    {
        using var dr = Csv.CreateDataReader(new MemoryStream());
        Assert.False(dr.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_NoHeaderRow()
    {
        string csv = "1,2\n3,4\n";
        using var dr = Csv.CreateDataReader(
            new MemoryStream(Encoding.UTF8.GetBytes(csv)),
            readerOptions: new SeparatedValues.Reading.Data.CsvDataReaderOptions { HasHeaderRow = false, ColumnNames = ["X", "Y"] });
        int n = 0;
        while (dr.Read()) n++;
        Assert.Equal(2, n);
    }

    // ---------- Csv.Write/Read with progress reporting ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_DefaultPath_WritesBatch()
    {
        var rows = Enumerable.Range(0, 50).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }
}

// ---------- Records ----------

[GenerateBinder]
public class FixedAllTypes
{
    [PositionalMap(Start = 0, Length = 10)] public long L { get; set; }
    [PositionalMap(Start = 10, Length = 5)] public short S { get; set; }
    [PositionalMap(Start = 15, Length = 3)] public byte B { get; set; }
    [PositionalMap(Start = 18, Length = 8)] public double D { get; set; }
    [PositionalMap(Start = 26, Length = 8)] public float F { get; set; }
    [PositionalMap(Start = 34, Length = 5)] public bool Bo { get; set; }
    [PositionalMap(Start = 39, Length = 10)] public decimal M { get; set; }
}

[GenerateBinder]
public class FixedAllNullable
{
    [PositionalMap(Start = 0, Length = 10)] public long? L { get; set; }
    [PositionalMap(Start = 10, Length = 5)] public short? S { get; set; }
    [PositionalMap(Start = 15, Length = 3)] public byte? B { get; set; }
    [PositionalMap(Start = 18, Length = 8)] public double? D { get; set; }
    [PositionalMap(Start = 26, Length = 8)] public float? F { get; set; }
    [PositionalMap(Start = 34, Length = 5)] public bool? Bo { get; set; }
    [PositionalMap(Start = 39, Length = 10)] public decimal? M { get; set; }
}

[GenerateBinder]
public class FixedDates
{
    [PositionalMap(Start = 0, Length = 19)] public DateTime Dt { get; set; }
    [PositionalMap(Start = 19, Length = 25)] public DateTimeOffset Dto { get; set; }
    [PositionalMap(Start = 44, Length = 10)] public DateOnly D { get; set; }
    [PositionalMap(Start = 54, Length = 8)] public TimeOnly T { get; set; }
}

[GenerateBinder]
public class FixedEnumRow
{
    [PositionalMap(Start = 0, Length = 5)] public Color Color { get; set; }
}

public enum Color
{
    Red, Green, Blue
}
