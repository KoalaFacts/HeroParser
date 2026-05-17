using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 26: CsvWriterBuilder fluent options, async streaming variants, FW writer builder.</summary>
public class CoveragePushTests26
{
    // ---------- CsvWriterBuilder<T> fluent variations ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WithDateOnlyFormat()
    {
        var rows = new[] { new DateOnlyRow { D = new DateOnly(2024, 6, 1) } };
        string csv = Csv.Write<DateOnlyRow>().WithDateOnlyFormat("yyyyMMdd").ToText(rows);
        Assert.Contains("20240601", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WithTimeOnlyFormat()
    {
        var rows = new[] { new TimeOnlyRow { T = new TimeOnly(12, 30, 45) } };
        string csv = Csv.Write<TimeOnlyRow>().WithTimeOnlyFormat("HHmmss").ToText(rows);
        Assert.Contains("123045", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WithDangerousChars()
    {
        var rows = new[] { new CoveragePerson { Name = "$DANGER", Age = 0 } };
        string csv = Csv.Write<CoveragePerson>()
            .WithInjectionProtection()
            .WithDangerousChars('$')
            .ToText(rows);
        Assert.Contains("'", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WithMaxColumnCount()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        string csv = Csv.Write<CoveragePerson>().WithMaxColumnCount(10).ToText(rows);
        Assert.NotEmpty(csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WithProgress()
    {
        int progressCalls = 0;
        var rows = Enumerable.Range(0, 100).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        string csv = Csv.Write<CoveragePerson>()
            .WithProgress(new Progress<CsvWriteProgress>(_ => Interlocked.Increment(ref progressCalls)))
            .WithProgressInterval(20)
            .ToText(rows);
        Assert.NotEmpty(csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_OnError_SkipRow()
    {
        var rows = new[] { new ThrowOnGet { StoredName = "Throw" }, new ThrowOnGet { StoredName = "Ok" } };
        string csv = Csv.Write<ThrowOnGet>()
            .OnError(ctx => SerializeErrorAction.SkipRow)
            .ToText(rows);
        Assert.Contains("Ok", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_OnError_WriteNull()
    {
        var rows = new[] { new ThrowOnGet { StoredName = "Throw" } };
        string csv = Csv.Write<ThrowOnGet>()
            .WithNullValue("NULL")
            .OnError(ctx => SerializeErrorAction.WriteNull)
            .ToText(rows);
        Assert.Contains("NULL", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WithCultureName()
    {
        var rows = new[] { new MoneyRow { Amount = 1234.56m } };
        string csv = Csv.Write<MoneyRow>().WithCulture("de-DE").ToText(rows);
        Assert.Contains("1234,56", csv);
    }

    // ---------- CsvWriterBuilder async streaming ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriter_ToStreamAsync_FromAsyncEnumerable()
    {
        static async IAsyncEnumerable<CoveragePerson> Source()
        {
            for (int i = 0; i < 20; i++)
            {
                yield return new CoveragePerson { Name = $"P{i}", Age = i };
                await Task.Yield();
            }
        }
        using var ms = new MemoryStream();
        await Csv.Write<CoveragePerson>().ToStreamAsync(ms, Source(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriter_ToFileAsync_FromAsyncEnumerable()
    {
        static async IAsyncEnumerable<CoveragePerson> Source()
        {
            yield return new CoveragePerson { Name = "Alice", Age = 30 };
            await Task.Yield();
        }
        string path = Path.GetTempFileName();
        try
        {
            await Csv.Write<CoveragePerson>().ToFileAsync(path, Source(), cancellationToken: TestContext.Current.CancellationToken);
            string content = File.ReadAllText(path);
            Assert.Contains("Alice", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriter_ToTextAsync_FromAsyncEnumerable()
    {
        static async IAsyncEnumerable<CoveragePerson> Source()
        {
            yield return new CoveragePerson { Name = "Alice", Age = 30 };
            await Task.Yield();
        }
        string csv = await Csv.Write<CoveragePerson>().ToTextAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Alice", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriter_ToStreamAsyncStreaming_FromAsyncEnumerable()
    {
        static async IAsyncEnumerable<CoveragePerson> Source()
        {
            for (int i = 0; i < 50; i++)
            {
                yield return new CoveragePerson { Name = $"P{i}", Age = i };
                await Task.Yield();
            }
        }
        using var ms = new MemoryStream();
        await Csv.Write<CoveragePerson>().ToStreamAsyncStreaming(ms, Source(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriter_ToStreamAsyncStreaming_FromIEnumerable()
    {
        var rows = Enumerable.Range(0, 50).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        using var ms = new MemoryStream();
        await Csv.Write<CoveragePerson>().ToStreamAsyncStreaming(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    // ---------- CsvWriterBuilder (non-generic) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_NonGeneric_CreateStreamWriter()
    {
        using var ms = new MemoryStream();
        using (var writer = Csv.Write().WithDelimiter(';').CreateStreamWriter(ms))
        {
            writer.WriteRow(["Name", "Age"]);
            writer.WriteRow(["Alice", "30"]);
        }
        string content = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Name;Age", content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_NonGeneric_CreateFileWriter()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var writer = Csv.Write().AlwaysQuote().CreateFileWriter(path))
            {
                writer.WriteRow(["a", "b"]);
            }
            string content = File.ReadAllText(path);
            Assert.Contains("\"a\"", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_NonGeneric_FluentChain()
    {
        var sw = new StringWriter();
        using (var writer = Csv.Write()
            .WithDelimiter(';')
            .WithQuote('\'')
            .WithNewLine("\r\n")
            .QuoteWhenNeeded()
            .WithCulture("en-US")
            .WithNullValue("NULL")
            .WithDateTimeFormat("o")
            .WithNumberFormat("F2")
            .WithMaxOutputSize(1_000_000)
            .WithMaxFieldSize(1000)
            .WithMaxColumnCount(10)
            .WithInjectionProtection()
            .CreateWriter(sw))
        {
            writer.WriteRow(["a", "b"]);
        }
        Assert.NotEmpty(sw.ToString());
    }

    // ---------- FixedWidth writer builder ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_Writer_Builder_WithCulture()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        string text = FixedWidth.Write<FixedAllTypes>().WithCulture("en-US").ToText(rows);
        Assert.NotEmpty(text);
    }

    // ---------- Csv.Read.cs ReadFromFile additional ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromFile_WithOptions()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "a;b\n1;2\n");
            using var reader = Csv.ReadFromFile(path, out _, new CsvReadOptions { Delimiter = ';' });
            int n = 0;
            while (reader.MoveNext()) n++;
            Assert.Equal(2, n);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromStream_WithOptions()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a;b\n1;2\n"));
        using var reader = Csv.ReadFromStream(stream, out _, new CsvReadOptions { Delimiter = ';' });
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromStream_LeaveOpenFalse()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n"));
        using (var reader = Csv.ReadFromStream(stream, out _, null, leaveOpen: false))
        {
            while (reader.MoveNext()) { }
        }
        Assert.False(stream.CanRead);
    }

    // ---------- CsvAsyncStreamReader extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvAsyncStreamReader_FromFileStatic_WithOptions()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "a;b\n1;2\n3;4\n");
            using var fs = File.OpenRead(path);
            await using var reader = Csv.CreateAsyncStreamReader(fs, new CsvReadOptions { Delimiter = ';' });
            int n = 0;
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(path); }
    }

    // ---------- FixedWidth StaticReader paths via ReadFromText ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ReadFromText_WithOptions()
    {
        var reader = FixedWidth.ReadFromText("rowdata\n", new FixedWidthReadOptions());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ReadFromByteSpan_WithOptions()
    {
        var reader = FixedWidth.ReadFromByteSpan("rowdata\n"u8.ToArray(), new FixedWidthReadOptions());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n);
    }

    // ---------- ExcelRecordWriter additional date types ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_DateOnlyType()
    {
        var rows = new[] { new DateOnlyRow { D = new DateOnly(2024, 6, 1) } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<DateOnlyRow>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_TimeOnlyType()
    {
        var rows = new[] { new TimeOnlyRow { T = new TimeOnly(12, 30, 45) } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<TimeOnlyRow>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    // ---------- CsvMultiSchemaBinder edge: byte-path discriminator > 8 chars ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_MultiSchema_LongDiscriminatorValue()
    {
        // Long discriminator value exercises byte-path key handling.
        string csv = "Type,A,B\nVeryLongTypeName,1,2\nAnotherVeryLong,3,4\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        int matched = 0;
        await using var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<FooRecord>("VeryLongTypeName")
            .OnUnmatchedRow(SeparatedValues.Reading.Records.MultiSchema.UnmatchedRowBehavior.Skip)
            .FromStream(ms);
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            if (reader.Current is FooRecord) matched++;
        }
        Assert.Equal(1, matched);
    }

    // ---------- Csv builder ParserOptions (max field size + max column count via builder) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WithMaxFieldSize_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = new string('x', 1000), Age = 1 } };
        Assert.Throws<CsvException>(() =>
            Csv.Write<CoveragePerson>().WithMaxFieldSize(10).ToText(rows));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_AllFluentChain()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string csv = Csv.Write<CoveragePerson>()
            .WithDelimiter(';')
            .WithQuote('\'')
            .WithNewLine("\r\n")
            .AlwaysQuote()
            .WithHeader()
            .WithCulture("en-US")
            .WithNullValue("NULL")
            .WithDateTimeFormat("o")
            .WithNumberFormat("F2")
            .WithMaxRowCount(1000)
            .WithMaxOutputSize(1_000_000)
            .WithMaxFieldSize(1000)
            .WithMaxColumnCount(100)
            .WithInjectionProtection(CsvInjectionProtection.EscapeWithQuote)
            .WithValidationMode(ValidationMode.Lenient)
            .ToText(rows);
        Assert.Contains("Alice", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_QuoteWhenNeeded()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string csv = Csv.Write<CoveragePerson>().QuoteWhenNeeded().ToText(rows);
        Assert.DoesNotContain("\"Alice\"", csv);
    }
}

[GenerateBinder]
public class DateOnlyRow
{
    public DateOnly D { get; set; }
}

[GenerateBinder]
public class TimeOnlyRow
{
    public TimeOnly T { get; set; }
}
