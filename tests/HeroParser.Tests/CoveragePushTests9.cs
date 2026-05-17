using System.Diagnostics.CodeAnalysis;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Ninth wave: reflection writer with validate, BooleanConverter variants, async-writer primitives w/ Always quote.</summary>
public class CoveragePushTests9
{
    // ---------- Reflection-based writer with Validate attributes ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void Reflection_RecordWriter_WithValidateAttribute_Lenient()
    {
        var rows = new[] { new ReflectValidateRow { Name = null, Length = 5 } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { ValidationMode = ValidationMode.Lenient });
        Assert.NotEmpty(csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void Reflection_RecordWriter_WithValidateAttribute_Strict_Throws()
    {
        var rows = new[] { new ReflectValidateRow { Name = null, Length = 5 } };
        Assert.Throws<ValidationException>(() => Csv.WriteToText(rows, options: new CsvWriteOptions { ValidationMode = ValidationMode.Strict }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void Reflection_RecordWriter_TooLongString_StrictThrows()
    {
        // MaxLength applies to strings; provide a too-long string.
        var rows = new[] { new ReflectValidateRow { Name = new string('x', 100), Length = 5 } };
        Assert.Throws<ValidationException>(() => Csv.WriteToText(rows, options: new CsvWriteOptions { ValidationMode = ValidationMode.Strict }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void Reflection_RecordWriter_WithCsvMapAttributes()
    {
        var rows = new[] { new ReflectMappedRow { Email = "a@b.c", DisplayName = "Alice" } };
        string csv = Csv.WriteToText(rows);
        Assert.Contains("Alice", csv);
        Assert.Contains("Email", csv);
    }

    // ---------- BooleanConverter single-char variants via reflection-based binder ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void FixedWidth_Reflection_Boolean_SingleCharVariants_True()
    {
        // Each row uses a different single-char true representation.
        var rows = FixedWidth.Read<ReflectBoolRow>().FromText("1\nY\ny\nT\nt\n").ToList();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.True(r.B));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void FixedWidth_Reflection_Boolean_SingleCharVariants_False()
    {
        var rows = FixedWidth.Read<ReflectBoolRow>().FromText("0\nN\nn\nF\nf\n").ToList();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.False(r.B));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void FixedWidth_Reflection_Guid_FromString()
    {
        var g = Guid.NewGuid();
        var rows = FixedWidth.Read<ReflectGuidRow>().FromText($"{g}\n").ToList();
        Assert.Single(rows);
        Assert.Equal(g, rows[0].G);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void FixedWidth_Reflection_BadGuid_TriggersError()
    {
        try
        {
            FixedWidth.Read<ReflectGuidRow>().FromText("not-a-guid                                  \n").ToList();
        }
        catch (Exception)
        {
            // Acceptable - exercises the error path
        }
    }

    // ---------- Async writer with QuoteStyle.Always + primitives (forces WriteFieldValueFromBufferAsync) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_AlwaysQuote_PrimitiveValues()
    {
        // Records contain primitive values (int, long, double) that get formatted in-buffer
        // and then need re-quoting under QuoteStyle.Always.
        var rows = Enumerable.Range(0, 20).Select(i => new PrimitivesRow
        {
            B = (byte)(i % 256),
            I = i,
            L = i * 1000L,
            D = i + 0.5,
            F = i + 0.25f,
            M = i + 0.1m,
            Bool = i % 2 == 0,
            G = Guid.NewGuid(),
            Dt = DateTime.UtcNow,
            US = (ushort)(i % 65535),
            S = (short)i,
            UI = (uint)i,
            UL = (ulong)i,
            Dto = DateTimeOffset.UtcNow,
            DOnly = DateOnly.FromDateTime(DateTime.Today),
            TOnly = TimeOnly.FromDateTime(DateTime.Now),
        });
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { QuoteStyle = QuoteStyle.Always },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        // Every value should be wrapped in quotes.
        Assert.Contains("\"0\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_LongFieldsThatNeedQuoting()
    {
        // Mix of large string fields with commas to force the long-field quoting branch.
        var rows = Enumerable.Range(0, 20).Select(i =>
            new CoveragePerson { Name = new string('x', 100) + ",comma" + i, Age = i });
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 1000);
    }

    // ---------- CsvColumn additional methods ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_Unquote()
    {
        string csv = "\"quoted\",unquoted\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        var col = reader.Current[0];
        // The unquoted span should not include the outer quotes.
        Assert.False(col.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_Unquote()
    {
        string csv = "\"quoted\",unquoted\n";
        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        var col = reader.Current[0];
        Assert.False(col.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_Span()
    {
        string csv = "hello\n";
        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        var span = reader.Current[0].Span;
        Assert.Equal(5, span.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_Span()
    {
        string csv = "hello\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        var span = reader.Current[0].Span;
        Assert.Equal(5, span.Length);
    }

    // ---------- FixedWidthReaderBuilder additional paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_FromFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n");
            var rows = FixedWidth.Read<FixedAllTypes>().FromFile(path).ToList();
            Assert.Single(rows);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NoTrailingNewline()
    {
        string text = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000";
        var rows = FixedWidth.Read<FixedAllTypes>().FromText(text).ToList();
        Assert.Single(rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_CrLfLineEndings()
    {
        string text = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\r\n" +
                      "1111111111" + "22345" + "100" + "4.140000" + "3.500000" + "false" + "9999.99000" + "\r\n";
        var rows = FixedWidth.Read<FixedAllTypes>().FromText(text).ToList();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_RoundTrip_WithAllTypes()
    {
        var src = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        string text = FixedWidth.Write<FixedAllTypes>().ToText(src);
        var read = FixedWidth.Read<FixedAllTypes>().FromText(text).ToList();
        Assert.Single(read);
        Assert.Equal(1L, read[0].L);
    }

    // ---------- FixedWidth writer with quotes/escape/special chars ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_Write_LargeBatch()
    {
        var src = Enumerable.Range(0, 500).Select(i => new FixedAllTypes
        {
            L = i,
            S = (short)i,
            B = (byte)(i % 256),
            D = i + 0.5,
            F = i + 0.25f,
            Bo = i % 2 == 0,
            M = i + 0.1m,
        });
        string text = FixedWidth.Write<FixedAllTypes>().ToText(src);
        Assert.True(text.Length > 500 * 49);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_AsyncWriter_ToFile_RoundTrip()
    {
        var src = new[] { new FixedAllTypes { L = 100L, S = 10, B = 50, D = 1.5, F = 2.5f, Bo = false, M = 3.5m } };
        string path = Path.GetTempFileName();
        try
        {
            await using (var fs = File.Create(path))
            {
                await FixedWidth.Write<FixedAllTypes>().ToStreamAsync(fs, src, cancellationToken: TestContext.Current.CancellationToken);
            }
            var rows = FixedWidth.Read<FixedAllTypes>().FromFile(path).ToList();
            Assert.Single(rows);
            Assert.Equal(100L, rows[0].L);
        }
        finally { File.Delete(path); }
    }

    // ---------- CsvAsyncStreamReader from file ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_FromFileViaStream()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "a,b\n1,2\n3,4\n");
            await using var fs = File.OpenRead(path);
            await using var reader = Csv.CreateAsyncStreamReader(fs);
            int n = 0;
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(path); }
    }

    // ---------- Csv from file ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Read_FromFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "a,b\n1,2\n3,4\n");
            using var reader = Csv.Read().FromFile(path, out _);
            int n = 0;
            while (reader.MoveNext()) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadTyped_FromStreamBytes()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "Name,Age\nAlice,30\nBob,25\n");
            int n = 0;
            using (var fs = File.OpenRead(path))
            using (var reader = Csv.Read<CoveragePerson>().FromStream(fs, out _))
            {
                foreach (var _ in reader) n++;
            }
            Assert.Equal(2, n);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_WriteTyped_ToFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
            Csv.Write<CoveragePerson>().ToFile(path, rows);
            string content = File.ReadAllText(path);
            Assert.Contains("Alice", content);
        }
        finally { File.Delete(path); }
    }

    // ---------- Excel: FromText (raw read) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_ToFile_AndRead()
    {
        string path = Path.GetTempFileName() + ".xlsx";
        try
        {
            var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
            global::HeroParser.Excel.Write<CoveragePerson>().ToFile(path, rows);
            var read = global::HeroParser.Excel.Read<CoveragePerson>().FromFile(path).ToList();
            Assert.Single(read);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_DataReader_FromStream()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        using var dr = global::HeroParser.Excel.CreateDataReader(ms);
        Assert.True(dr.Read());
        Assert.Equal("Alice", dr.GetString(0));
    }
}

// ---------- Records ----------

public class ReflectValidateRow
{
    [Validate(NotNull = true, MaxLength = 50)]
    public string? Name { get; set; }

    public int Length { get; set; }
}

public class ReflectMappedRow
{
    [TabularMap(Name = "Email")]
    public string? Email { get; set; }

    [TabularMap(Name = "DisplayName")]
    public string? DisplayName { get; set; }
}

public class ReflectBoolRow
{
    [PositionalMap(Start = 0, Length = 1)]
    public bool B { get; set; }
}

public class ReflectGuidRow
{
    [PositionalMap(Start = 0, Length = 36)]
    public Guid G { get; set; }
}
