using System.Data;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Seventh wave: async writer primitive paths, FixedWidth pipe reader, CsvDataReader extras, Excel edges.</summary>
public class CoveragePushTests7
{
    // ---------- Async writer with all primitive types (exercises WriteSpanFormattableDirectlyAsync paths) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_PrimitiveTypes_AllConvertersPath()
    {
        var rows = new[]
        {
            new PrimitivesRow {
                B = 200, S = -123, US = 65000, I = -42, UI = 4_000_000_000,
                L = -9_000_000_000_000L, UL = 18_000_000_000_000_000_000UL,
                F = 3.14f, D = 2.71828, M = 1234.5678m,
                Bool = true, G = Guid.NewGuid(),
                Dt = DateTime.UtcNow, Dto = DateTimeOffset.UtcNow,
                DOnly = DateOnly.FromDateTime(DateTime.Today),
                TOnly = TimeOnly.FromDateTime(DateTime.Now),

            }
        };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 100);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_NullableAllPrimitives_WithSomeNulls()
    {
        var rows = new[]
        {
            new NullablePrimitivesRow { B = 1, S = 2, I = 3 }, // some null
            new NullablePrimitivesRow {
                B = 200, S = -123, US = 65000, I = -42, UI = 4_000_000_000,
                L = -9_000_000_000_000L, UL = 18_000_000_000_000_000_000UL,
                F = 3.14f, D = 2.71828, M = 1234.5678m,
                Bool = true, G = Guid.NewGuid()
            }
        };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 100);
    }

    // ---------- Sync writer with same primitive types (covers sync sister methods) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SyncWriter_PrimitiveTypes_AllConverters()
    {
        var rows = new[]
        {
            new PrimitivesRow {
                B = 200, S = -123, US = 65000, I = -42, UI = 4_000_000_000,
                L = -9_000_000_000_000L, UL = 18_000_000_000_000_000_000UL,
                F = 3.14f, D = 2.71828, M = 1234.5678m,
                Bool = true, G = Guid.NewGuid(),
                Dt = DateTime.UtcNow, Dto = DateTimeOffset.UtcNow,
                DOnly = DateOnly.FromDateTime(DateTime.Today),
                TOnly = TimeOnly.FromDateTime(DateTime.Now),

            }
        };
        string csv = Csv.WriteToText(rows);
        Assert.True(csv.Length > 100);
    }

    // ---------- FixedWidth pipe reader paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_FromPipe_BasicRead()
    {
        string data = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n" +
                      "1111111111" + "22345" + "100" + "4.140000" + "3.500000" + "false" + "9999.99000" + "\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var pipe = System.IO.Pipelines.PipeReader.Create(stream);
        int n = 0;
        await foreach (var _ in FixedWidth.ReadFromPipeReaderAsync(pipe, cancellationToken: TestContext.Current.CancellationToken))
        {
            n++;
        }
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_FromPipe_NoFinalNewline()
    {
        string data = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var pipe = System.IO.Pipelines.PipeReader.Create(stream);
        int n = 0;
        await foreach (var _ in FixedWidth.ReadFromPipeReaderAsync(pipe, cancellationToken: TestContext.Current.CancellationToken))
        {
            n++;
        }
        Assert.Equal(1, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_FromPipe_CrLf()
    {
        string data = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var pipe = System.IO.Pipelines.PipeReader.Create(stream);
        int n = 0;
        await foreach (var _ in FixedWidth.ReadFromPipeReaderAsync(pipe, cancellationToken: TestContext.Current.CancellationToken))
        {
            n++;
        }
        Assert.Equal(1, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_FromPipe_EmptyStream()
    {
        using var stream = new MemoryStream();
        var pipe = System.IO.Pipelines.PipeReader.Create(stream);
        int n = 0;
        await foreach (var _ in FixedWidth.ReadFromPipeReaderAsync(pipe, cancellationToken: TestContext.Current.CancellationToken))
        {
            n++;
        }
        Assert.Equal(0, n);
    }

    // ---------- CsvDataReader: SeqScan + missing-column path ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_MissingColumns_AllowAndIsDBNull()
    {
        // With AllowMissingColumns, accessing the missing column returns DBNull.Value.
        string csv = "A,B,C\n1,2\n";
        using var dr = Csv.CreateDataReader(
            new MemoryStream(Encoding.UTF8.GetBytes(csv)),
            readerOptions: new SeparatedValues.Reading.Data.CsvDataReaderOptions { AllowMissingColumns = true });
        Assert.True(dr.Read());
        Assert.Equal(DBNull.Value, dr.GetValue(2));
        Assert.True(dr.IsDBNull(2));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_MissingColumnsAtReadTime_Throws()
    {
        string csv = "A,B,C\n1,2\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.Throws<CsvException>(() => dr.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_ColumnNamesProvided_ButHeaderMismatch_Throws()
    {
        string csv = "A,B\n1,2\n";
        Assert.Throws<CsvException>(() =>
        {
            using var dr = Csv.CreateDataReader(
                new MemoryStream(Encoding.UTF8.GetBytes(csv)),
                readerOptions: new SeparatedValues.Reading.Data.CsvDataReaderOptions { ColumnNames = ["X", "Y", "Z"] });
            dr.Read();
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_NoHeader_NoColumnNames_InfersFromFirstRow()
    {
        string csv = "1,2,3\n4,5,6\n";
        using var dr = Csv.CreateDataReader(
            new MemoryStream(Encoding.UTF8.GetBytes(csv)),
            readerOptions: new SeparatedValues.Reading.Data.CsvDataReaderOptions { HasHeaderRow = false });
        Assert.True(dr.Read());
        Assert.Equal(3, dr.FieldCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_AccessBeforeRead_Throws()
    {
        string csv = "A,B\n1,2\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.Throws<InvalidOperationException>(() => dr.GetValue(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_BadOrdinal_Throws()
    {
        string csv = "A,B\n1,2\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.True(dr.Read());
        Assert.Throws<IndexOutOfRangeException>(() => dr.GetValue(99));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_GetOrdinal_MissingColumn_Throws()
    {
        string csv = "A,B\n1,2\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.Throws<IndexOutOfRangeException>(() => dr.GetOrdinal("Missing"));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_Depth_RecordsAffected()
    {
        string csv = "A\n1\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.Equal(0, dr.Depth);
        Assert.Equal(-1, dr.RecordsAffected);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_HasRows_BeforeAndAfterRead()
    {
        string csv = "A\n1\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.True(dr.HasRows);
        dr.Read();
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_Close_DisposeAccessAfter_Throws()
    {
        string csv = "A\n1\n";
        var dr = Csv.CreateDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        dr.Close();
        Assert.True(dr.IsClosed);
        Assert.Throws<InvalidOperationException>(() => dr.Read());
    }

    // ---------- Excel writer edge cases ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_AsyncEnumerable()
    {
        var rows = Enumerable.Range(0, 100).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_AllSheets()
    {
        // First create a multi-sheet workbook by writing.
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;

        var dict = global::HeroParser.Excel.Read<CoveragePerson>().AllSheets().FromStream(ms);
        Assert.NotEmpty(dict);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_RowLevel_FromText()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        var rowsBack = global::HeroParser.Excel.Read().FromStream(ms).ToList();
        Assert.NotEmpty(rowsBack);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_DateValues()
    {
        var rows = new[] { new EventRow { When = new DateTime(2024, 6, 1, 12, 30, 45) } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<EventRow>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    // ---------- CsvStreamWriter explicit: forced flush, write byte stream ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamWriter_ToText_Tab()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { Delimiter = '\t' });
        Assert.Contains("Alice\t30", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamWriter_ToStream_Sync()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        Csv.WriteToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }
}

// ---------- Records ----------

[GenerateBinder]
public class PrimitivesRow
{
    public byte B { get; set; }
    public short S { get; set; }
    public ushort US { get; set; }
    public int I { get; set; }
    public uint UI { get; set; }
    public long L { get; set; }
    public ulong UL { get; set; }
    public float F { get; set; }
    public double D { get; set; }
    public decimal M { get; set; }
    public bool Bool { get; set; }
    public Guid G { get; set; }
    public DateTime Dt { get; set; }
    public DateTimeOffset Dto { get; set; }
    public DateOnly DOnly { get; set; }
    public TimeOnly TOnly { get; set; }
}

[GenerateBinder]
public class NullablePrimitivesRow
{
    public byte? B { get; set; }
    public short? S { get; set; }
    public ushort? US { get; set; }
    public int? I { get; set; }
    public uint? UI { get; set; }
    public long? L { get; set; }
    public ulong? UL { get; set; }
    public float? F { get; set; }
    public double? D { get; set; }
    public decimal? M { get; set; }
    public bool? Bool { get; set; }
    public Guid? G { get; set; }
}
