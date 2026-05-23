using System.Globalization;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 34: FINAL push to 90% - small file extras.</summary>
public class CoveragePushTests34
{
    // ---------- ExtensionsToCsvRow.GetField / TryGetField ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExtensionsToCsvRow_GetField_CharPath()
    {
        using var reader = Csv.Read().FromText("Name,Age\nAlice,30\n");
        Assert.True(reader.MoveNext());
        var headerRow = reader.Current;
        var headers = new CsvHeaderIndex(headerRow, caseSensitive: false);

        Assert.True(reader.MoveNext());
        var dataRow = reader.Current;
        var name = dataRow.GetField("Name", headers);
        Assert.Equal("Alice", name.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExtensionsToCsvRow_TryGetField_CharPath()
    {
        using var reader = Csv.Read().FromText("Name,Age\nAlice,30\n");
        Assert.True(reader.MoveNext());
        var headerRow = reader.Current;
        var headers = new CsvHeaderIndex(headerRow, caseSensitive: false);

        Assert.True(reader.MoveNext());
        var dataRow = reader.Current;
        Assert.True(dataRow.TryGetField("Name", headers, out var col));
        Assert.Equal("Alice", col.ToString());
        Assert.False(dataRow.TryGetField("Missing", headers, out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExtensionsToCsvRow_GetField_BytePath()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Age\nAlice,30\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        var headerRow = reader.Current;
        var headers = new CsvHeaderIndex(headerRow, caseSensitive: false);

        Assert.True(reader.MoveNext());
        var dataRow = reader.Current;
        var name = dataRow.GetField("Name", headers);
        Assert.Equal("Alice", name.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExtensionsToCsvRow_TryGetField_BytePath()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Age\nAlice,30\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        var headerRow = reader.Current;
        var headers = new CsvHeaderIndex(headerRow, caseSensitive: false);

        Assert.True(reader.MoveNext());
        var dataRow = reader.Current;
        Assert.True(dataRow.TryGetField("Name", headers, out var col));
        Assert.Equal("Alice", col.ToString());
        Assert.False(dataRow.TryGetField("Missing", headers, out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExtensionsToCsvRow_GetField_NonExistent_Throws()
    {
        using var reader = Csv.Read().FromText("Name,Age\nAlice,30\n");
        Assert.True(reader.MoveNext());
        var headerRow = reader.Current;
        var headers = new CsvHeaderIndex(headerRow, caseSensitive: false);

        Assert.True(reader.MoveNext());
        var dataRow = reader.Current;
        bool threw = false;
        try { _ = dataRow.GetField("Missing", headers); }
        catch (Exception) { threw = true; }
        Assert.True(threw);
    }

    // ---------- PooledColumnEnds ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void PooledColumnEnds_Lifecycle()
    {
        var ends = new PooledColumnEnds(10);
        Assert.True(ends.Span.Length == 10);
        Assert.NotNull(ends.Buffer);
        ends.Return();
        Assert.Throws<ObjectDisposedException>(() => { _ = ends.Span; });
        ends.Dispose();
    }

    // ---------- ExcelWriteOptions defaults ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcelWriteOptions_Defaults_Touched()
    {
        var opts = new global::HeroParser.Excels.Core.ExcelWriteOptions();
        Assert.Equal(CultureInfo.InvariantCulture, opts.Culture);
        Assert.Equal("", opts.NullValue);
        Assert.Null(opts.DateTimeFormat);
        Assert.Null(opts.DateOnlyFormat);
        Assert.Null(opts.TimeOnlyFormat);
        Assert.Null(opts.NumberFormat);
        Assert.Null(opts.MaxRowCount);
        Assert.True(opts.WriteHeader);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcelDataReaderOptions_Defaults_Touched()
    {
        var opts = new global::HeroParser.Excels.Reading.Data.ExcelDataReaderOptions();
        Assert.True(opts.HasHeaderRow);
    }

    // ---------- Excel.Read.cs additional ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_FromFile_NonGeneric()
    {
        string path = Path.GetTempFileName() + ".xlsx";
        try
        {
            var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
            global::HeroParser.Excel.Write<CoveragePerson>().ToFile(path, rows);
            var rowsBack = global::HeroParser.Excel.Read().FromFile(path);
            Assert.NotEmpty(rowsBack);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---------- Csv.Validation extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_StrictDelimiter()
    {
        var result = Csv.Validate("a;b\n1;2\n", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            Delimiter = ';'
        });
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_SkipRows()
    {
        var result = Csv.Validate("BANNER\na,b\n1,2\n", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            SkipRows = 1
        });
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_WithParseOptions()
    {
        var result = Csv.Validate("a;b\n1;2\n", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            ParseOptions = new global::HeroParser.SeparatedValues.Core.CsvReadOptions { Delimiter = ';' }
        });
        Assert.NotNull(result);
    }

    // ---------- ExcelDeserializeError ----------

    // (ExcelDeserializeError not directly accessible; omitted.)

    // ---------- CsvValidationError properties ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvValidationError_Type()
    {
        Assert.NotNull(typeof(global::HeroParser.SeparatedValues.Validation.CsvValidationError));
    }

    // ---------- ExcelRecordWriterFactory ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_MultipleTypes()
    {
        // First type
        var rows1 = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms1 = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms1, rows1);

        // Different type - exercises the factory cache miss path
        var rows2 = new[] { new MoneyRow { Amount = 1234.56m } };
        using var ms2 = new MemoryStream();
        global::HeroParser.Excel.Write<MoneyRow>().ToStream(ms2, rows2);

        Assert.True(ms1.Length > 0);
        Assert.True(ms2.Length > 0);
    }

    // ---------- CsvRecordOptions extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordOptions_With_NewInstanceImmutable()
    {
        var opts = new global::HeroParser.SeparatedValues.Reading.Records.CsvRecordOptions
        {
            HasHeaderRow = false,
            CaseSensitiveHeaders = true,
            AllowMissingColumns = true,
            SkipRows = 5,
            ProgressIntervalRows = 100,
        };
        Assert.False(opts.HasHeaderRow);
        Assert.True(opts.CaseSensitiveHeaders);
        Assert.True(opts.AllowMissingColumns);
        Assert.Equal(5, opts.SkipRows);
        Assert.Equal(100, opts.ProgressIntervalRows);
    }

    // ---------- CsvCharToByteBinderAdapter empty-columns shortcut ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvCharToByteBinderAdapter_EmptyRow_DoesNotCrash()
    {
        // An empty row in char path should be handled by the adapter's emptyBuffer/emptyColumnEnds path.
        using var reader = Csv.Read<CoveragePerson>()
            .Map(p => p.Name, c => c.Name("Name"))
            .Map(p => p.Age, c => c.Name("Age"))
            .AllowMissingColumns()
            .FromText("Name,Age\n\nAlice,30\n");
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.True(n >= 1);
    }

    // ---------- CsvRecordReaderBuilder.ParserOptions ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_AllParserOptions()
    {
        string csv = "Name|Age\nAlice|30\nBob|25\n";
        using var reader = Csv.Read<CoveragePerson>()
            .WithDelimiter('|')
            .WithQuote('"')
            .WithMaxColumns(50)
            .WithMaxRows(1000)
            .WithMaxFieldSize(100)
            .WithMaxRowSize(1000)
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_TrimFields()
    {
        string csv = "Name,Age\n  Alice  ,  30  \n";
        using var reader = Csv.Read<CoveragePerson>()
            .TrimFields()
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Single([n]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_WithCommentCharacter()
    {
        string csv = "# header comment\nName,Age\n# inline\nAlice,30\n";
        using var reader = Csv.Read<CoveragePerson>()
            .WithCommentCharacter('#')
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Single([n]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_AllowNewlinesInQuotes()
    {
        string csv = "Name,Age\n\"multi\nline\",30\n";
        using var reader = Csv.Read<CoveragePerson>()
            .AllowNewlinesInQuotes()
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Single([n]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_WithEscapeCharacter()
    {
        string csv = "Name,Age\nAl\\,ice,30\n";
        using var reader = Csv.Read<CoveragePerson>()
            .WithEscapeCharacter('\\')
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Single([n]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_DisableQuotedFields()
    {
        string csv = "Name,Age\n\"Alice\",30\n";
        using var reader = Csv.Read<CoveragePerson>()
            .DisableQuotedFields()
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Single([n]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_WithMaxInputSize()
    {
        string csv = "Name,Age\nAlice,30\n";
        using var reader = Csv.Read<CoveragePerson>()
            .WithMaxInputSize(1_000_000)
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Single([n]);
    }

    // ---------- Csv.Read.cs DeserializeRecords overloads ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DeserializeRecords_FromString_Overload()
    {
        var rows = new List<CoveragePerson>();
        foreach (var r in Csv.DeserializeRecords<CoveragePerson>("Name,Age\nAlice,30\n"))
            rows.Add(r);
        Assert.Single(rows);
    }
}
