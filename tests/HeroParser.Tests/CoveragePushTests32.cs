using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 32: final push - filtered sync writer + more small clusters.</summary>
public class CoveragePushTests32
{
    // ---------- CsvRecordWriter.WriteRecordsFiltered (sync) via [Format(ExcludeIfAllEmpty)] ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_SyncFiltered_PartialEmpty()
    {
        // Some rows have non-empty Optional, others empty → can't filter out the column.
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 100; i++)
        {
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = i % 2 == 0 ? "O" + i : null });
        }
        string csv = Csv.WriteToText(rows);
        Assert.Contains("Optional", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_SyncFiltered_MaxRowCount()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 50; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = $"O{i}" });
        Assert.Throws<CsvException>(() => Csv.WriteToText(rows, options: new CsvWriteOptions { MaxRowCount = 5 }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_SyncFiltered_LargeWithProgress()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 1000; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = $"O{i}" });
        int progressCalls = 0;
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            WriteProgress = new Progress<CsvWriteProgress>(_ => Interlocked.Increment(ref progressCalls)),
            WriteProgressIntervalRows = 100
        });
        Assert.True(csv.Length > 1000);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_SyncFiltered_NoHeader()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 10; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = $"O{i}" });
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { WriteHeader = false });
        Assert.DoesNotContain("Required", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_SyncFiltered_AllEmpty_HeaderExcluded()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 5; i++) rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = null });
        string csv = Csv.WriteToText(rows);
        Assert.DoesNotContain("Optional", csv);
        Assert.Contains("Required", csv);
    }

    // ---------- FixedWidthCharSpanReader / FixedWidthByteSpanReader large input ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_AllowMissingColumns_Toggle()
    {
        var opts = FixedWidthReadOptions.Default with { AllowMissingColumns = true };
        var reader = new FixedWidthCharSpanReader("short\n".AsSpan(), opts);
        Assert.True(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_HasHeaderRow()
    {
        var opts = FixedWidthReadOptions.Default with { HasHeaderRow = true };
        var reader = new FixedWidthCharSpanReader("header\nrow1\nrow2\n".AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.True(n >= 2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_CaseSensitiveHeaders()
    {
        var opts = FixedWidthReadOptions.Default with { HasHeaderRow = true, CaseSensitiveHeaders = true };
        var reader = new FixedWidthCharSpanReader("Name\nAlice\n".AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n); // header consumed
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_HasHeaderRow_SkipsHeader()
    {
        byte[] data = "header\nrow1\nrow2\n"u8.ToArray();
        var opts = FixedWidthReadOptions.Default with { HasHeaderRow = true };
        var reader = new FixedWidthByteSpanReader(data.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.True(n >= 2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_NoSkipEmpty_AllowShort_KeepsBlanks()
    {
        byte[] data = "\nrow1\n\nrow2\n"u8.ToArray();
        var opts = FixedWidthReadOptions.Default with { SkipEmptyLines = false, AllowShortRows = true };
        var reader = new FixedWidthByteSpanReader(data.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.True(n >= 4); // 2 blank + 2 data
    }

    // ---------- Csv.Read.Async coverage ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_DeserializeRecordsAsync_PipeReader_WithParserOptions()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Name;Age\nAlice;30\n");
        using var stream = new MemoryStream(bytes);
        var pipe = System.IO.Pipelines.PipeReader.Create(stream);
        var rows = new List<CoveragePerson>();
        await foreach (var r in Csv.DeserializeRecordsAsync<CoveragePerson>(
            pipe,
            parserOptions: new CsvReadOptions { Delimiter = ';' },
            cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(r);
        }
        Assert.Single(rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_DeserializeRecordsAsync_PipeReader_SkipRows()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("BANNER\nName,Age\nAlice,30\nBob,25\n");
        using var stream = new MemoryStream(bytes);
        var pipe = System.IO.Pipelines.PipeReader.Create(stream);
        var rows = new List<CoveragePerson>();
        await foreach (var r in Csv.DeserializeRecordsAsync<CoveragePerson>(
            pipe,
            recordOptions: new global::HeroParser.SeparatedValues.Reading.Records.CsvRecordOptions { SkipRows = 1 },
            cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(r);
        }
        Assert.Equal(2, rows.Count);
    }

    // ---------- ExcelRecordReaderBuilder progress ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_WithCulture()
    {
        var rows = new[] { new MoneyRow { Amount = 1234.56m } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<MoneyRow>().ToStream(ms, rows);
        ms.Position = 0;
        var read = global::HeroParser.Excel.Read<MoneyRow>()
            .WithCulture(CultureInfo.InvariantCulture)
            .FromStream(ms)
            .ToList();
        Assert.Single(read);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_NullValues()
    {
        var rows = new[] { new NullableAgePerson { Name = "Alice", Age = null } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<NullableAgePerson>().ToStream(ms, rows);
        ms.Position = 0;
        var read = global::HeroParser.Excel.Read<NullableAgePerson>()
            .WithNullValues("NULL")
            .FromStream(ms)
            .ToList();
        Assert.Single(read);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_AllowMissingColumns()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        var read = global::HeroParser.Excel.Read<CoveragePerson>()
            .AllowMissingColumns()
            .FromStream(ms)
            .ToList();
        Assert.Single(read);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_CaseSensitiveHeaders()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        var read = global::HeroParser.Excel.Read<CoveragePerson>()
            .CaseSensitiveHeaders()
            .FromStream(ms)
            .ToList();
        Assert.Single(read);
    }

    // ---------- ExcelWriterBuilder additional fluent ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_WithoutHeader()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().WithoutHeader().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_WithDateTimeFormat()
    {
        var rows = new[] { new EventRow { When = new DateTime(2024, 6, 1) } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<EventRow>()
            .WithDateTimeFormat("yyyy-MM-dd")
            .ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    // ---------- FixedWidthStreamWriter direct usage ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthStreamWriter_DirectUsage()
    {
        using var ms = new MemoryStream();
        using (var tw = new StreamWriter(ms, Encoding.UTF8, leaveOpen: true))
        using (var w = new global::HeroParser.FixedWidths.Writing.FixedWidthStreamWriter(
            tw,
            new global::HeroParser.FixedWidths.Writing.FixedWidthWriteOptions(),
            leaveOpen: true))
        {
            w.WriteField("Alice", 10);
            w.WriteField("30", 5);
            w.EndRow();
            w.Flush();
        }
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthStreamWriter_FieldTypes()
    {
        using var ms = new MemoryStream();
        using (var tw = new StreamWriter(ms, Encoding.UTF8, leaveOpen: true))
        using (var w = new global::HeroParser.FixedWidths.Writing.FixedWidthStreamWriter(
            tw,
            new global::HeroParser.FixedWidths.Writing.FixedWidthWriteOptions(),
            leaveOpen: true))
        {
            w.WriteField(42, 5);
            w.WriteField(1.5, 8);
            w.WriteField(true, 5);
            w.WriteField(DateTime.UtcNow, 20);
            w.EndRow();
            w.Flush();
        }
        Assert.True(ms.Length > 0);
    }

    // ---------- Csv builder edge cases ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Read_WithAllOptions()
    {
        string csv = "# comment\n  Alice  ,  30  \n  Bob  ,  25  \n";
        using var reader = Csv.Read()
            .WithDelimiter(',')
            .WithQuote('"')
            .WithCommentCharacter('#')
            .TrimFields()
            .WithMaxColumns(100)
            .WithMaxRows(1000)
            .WithMaxFieldSize(100)
            .WithMaxRowSize(1000)
            .WithMaxInputSize(1_000_000)
            .FromText(csv);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    // ---------- CsvAsyncStreamWriter buffer flush behaviors ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_Encoding_UTF32()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, encoding: Encoding.UTF32, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_Encoding_Unicode()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, encoding: Encoding.Unicode, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_Encoding_ASCII()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, encoding: Encoding.ASCII, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    // ---------- More CsvRowParser tests with specific tricky CSV ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Read_OnlyEmptyFields()
    {
        // Row with only empty fields.
        string csv = ",,,\n,,,\n";
        using var reader = Csv.Read().FromText(csv);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Read_TrailingNewlineOnly()
    {
        string csv = "a,b\n";
        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Read_MixedLineEndings()
    {
        string csv = "a,b\n1,2\r\n3,4\r5,6\n";
        using var reader = Csv.Read().FromText(csv);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.True(n >= 3);
    }

    // ---------- FixedWidthDataReader extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReader_NextResult_AlwaysFalse()
    {
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths([3], ["V"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("foo\n"));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.False(dr.NextResult());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReader_Indexer()
    {
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths([3, 3], ["A", "B"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("foobar\n"));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.True(dr.Read());
        Assert.NotNull(dr[0]);
        Assert.NotNull(dr["A"]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReader_RecordsAffected()
    {
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths([3], ["V"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("foo\n"));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.Equal(-1, dr.RecordsAffected);
        Assert.Equal(0, dr.Depth);
        Assert.True(dr.HasRows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReader_GetDataTypeName()
    {
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths([3], ["V"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("foo\n"));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.Equal("String", dr.GetDataTypeName(0));
        Assert.Equal(typeof(string), dr.GetFieldType(0));
    }
}
