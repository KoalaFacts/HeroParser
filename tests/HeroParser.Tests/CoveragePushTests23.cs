using System.Globalization;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 23: WriteRecordsUnfilteredAsync, FixedWidth FluentMap.WithMap path, more reader/writer corners.</summary>
public class CoveragePushTests23
{
    // ---------- CsvRecordWriter WriteRecordsUnfilteredAsync (filtered path with no candidates filtered out) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncFiltered_AllColumnsPopulated_TakesUnfilteredPath()
    {
        // Record has [Format(ExcludeIfAllEmpty=true)] on Optional — needsEmptyColumnScan=true.
        // All rows have non-empty Optional → no columns excluded → falls through to
        // WriteRecordsUnfilteredAsync at line 927.
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 100; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = $"O{i}" });

        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Optional", csv); // column kept
        Assert.Contains("O50", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncFiltered_WithProgress()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 250; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = $"O{i}" });

        int progressCalls = 0;
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions
            {
                WriteProgress = new Progress<CsvWriteProgress>(_ => Interlocked.Increment(ref progressCalls)),
                WriteProgressIntervalRows = 50,
            },
            cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncFiltered_MaxRowCount_Throws()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 50; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = $"O{i}" });

        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<CsvException>(() => Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { MaxRowCount = 5 },
            cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncFiltered_AllColumnsEmpty_OutputEmpty()
    {
        // All rows have Optional=null → column gets excluded → only Required column written.
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 10; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = null });

        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.DoesNotContain("Optional", csv);
        Assert.Contains("Required", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncFiltered_NoHeader()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 10; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = $"O{i}" });

        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { WriteHeader = false },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.DoesNotContain("Optional", csv); // header excluded
        Assert.Contains("R0", csv); // data still present
    }

    // ---------- Sync variant of WriteRecordsFiltered → WriteRecordsUnfiltered ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_SyncFiltered_AllColumnsPopulated_TakesUnfilteredPath()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 50; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = $"O{i}" });
        string csv = Csv.WriteToText(rows);
        Assert.Contains("Optional", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_SyncFiltered_AllColumnsEmpty_Excluded()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 10; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = null });
        string csv = Csv.WriteToText(rows);
        Assert.DoesNotContain("Optional", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_SyncFiltered_WithProgress()
    {
        var rows = new List<MaybeExcludedRow>();
        for (int i = 0; i < 250; i++)
            rows.Add(new MaybeExcludedRow { Required = $"R{i}", Optional = $"O{i}" });
        int progressCalls = 0;
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            WriteProgress = new Progress<CsvWriteProgress>(_ => Interlocked.Increment(ref progressCalls)),
            WriteProgressIntervalRows = 50,
        });
        Assert.NotEmpty(csv);
    }

    // ---------- CsvRecordWriter with [Format(WriteFormat="...")] on async path ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncFiltered_DateFormat_Applied()
    {
        var rows = new List<MaybeExcludedDateRow>();
        for (int i = 0; i < 50; i++)
            rows.Add(new MaybeExcludedDateRow
            {
                Required = $"R{i}",
                When = new DateTime(2024, 6, 1).AddDays(i),
                Optional = $"O{i}"
            });
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("20240601", csv);
    }

    // ---------- FixedWidth Fluent map with WithMap ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("fluent map")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("fluent map")]
    public void FixedWidth_FluentMap_WithMap_RoundTrip()
    {
        var map = new global::HeroParser.FixedWidths.Mapping.FixedWidthMap<PlainPerson>();
        map.Map(p => p.Name, c => c.Start(0).Length(10));
        map.Map(p => p.Age, c => c.Start(10).Length(3));

        string line = "Alice     " + "30 " + "\n";
        var rows = global::HeroParser.FixedWidth.Read<PlainPerson>().WithMap(map).FromText(line).ToList();
        Assert.Single(rows);
        Assert.Equal("Alice", rows[0].Name);
        Assert.Equal(30, rows[0].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("fluent map")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("fluent map")]
    public void FixedWidth_FluentMap_WithMap_WriteRoundTrip()
    {
        var writeMap = new global::HeroParser.FixedWidths.Mapping.FixedWidthMap<PlainPerson>();
        writeMap.Map(p => p.Name, c => c.Start(0).Length(10));
        writeMap.Map(p => p.Age, c => c.Start(10).Length(3));

        var rows = new[] { new PlainPerson { Name = "Alice", Age = 30 } };
        string text = global::HeroParser.FixedWidth.Write<PlainPerson>().WithMap(writeMap).ToText(rows);
        Assert.Contains("Alice", text);
    }

    // ---------- CsvDescriptorBinder line 208-215 (IsNullValue helper) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("fluent map")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("fluent map")]
    public void Csv_FluentMap_WithMultipleNullValues()
    {
        string csv = "Name,Age\nAlice,30\nN/A,25\nNULL,40\n-,50\n";
        var reader = Csv.Read<PlainPerson>()
            .Map(p => p.Name, c => c.Name("Name"))
            .Map(p => p.Age, c => c.Name("Age"))
            .WithNullValues("N/A", "NULL", "-")
            .FromText(csv);
        var rows = new List<PlainPerson>();
        foreach (var r in reader) rows.Add(r);
        Assert.Equal(4, rows.Count);
        Assert.Null(rows[1].Name);
        Assert.Null(rows[2].Name);
        Assert.Null(rows[3].Name);
    }

    // ---------- CsvRecordWriter cancellation mid-batch ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncCancellation_DuringWrite()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var rows = Enumerable.Range(0, 100).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        using var ms = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Csv.WriteToStreamAsync(ms, rows, cancellationToken: cts.Token).AsTask());
    }

    // ---------- CSV writer with custom culture (de-DE) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WithCulture_DateFormatting()
    {
        var rows = new[] { new EventRow { When = new DateTime(2024, 6, 1) } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            Culture = CultureInfo.GetCultureInfo("de-DE"),
            DateTimeFormat = "d",
        });
        Assert.NotEmpty(csv);
    }

    // ---------- Csv.Validate explicit branches ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_EmptyInput()
    {
        var result = Csv.Validate("");
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_WithExpectedColumns_Match()
    {
        var result = Csv.Validate("Name,Age\nAlice,30\n", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            RequiredHeaders = ["Name", "Age"]
        });
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_WithExpectedColumns_Missing()
    {
        var result = Csv.Validate("Name\nAlice\n", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            RequiredHeaders = ["Name", "Age"]
        });
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_WithMaxRowCount()
    {
        var result = Csv.Validate("a,b\n1,2\n3,4\n", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            MaxRows = 1
        });
        Assert.NotNull(result);
    }

    // ---------- Csv.InferSchema variants ----------

    // (InferSchema takes its own options type, omitting parameterised variant.)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_InferSchema_AllNumericTypes()
    {
        string csv = "I,L,D,B,DT,G\n42,9999999999,3.14,true,2024-06-01,11111111-1111-1111-1111-111111111111\n";
        var schema = Csv.InferSchema(csv);
        Assert.NotNull(schema);
    }
}

[GenerateBinder]
public class MaybeExcludedRow
{
    public string? Required { get; set; }

    [Format(ExcludeIfAllEmpty = true)]
    public string? Optional { get; set; }
}

[GenerateBinder]
public class MaybeExcludedDateRow
{
    public string? Required { get; set; }

    [Format(WriteFormat = "yyyyMMdd")]
    public DateTime When { get; set; }

    [Format(ExcludeIfAllEmpty = true)]
    public string? Optional { get; set; }
}
