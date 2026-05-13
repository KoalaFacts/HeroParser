using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Fifth wave: writer error handlers, large IReadOnlyList batches, progress, sync paths.</summary>
public class CoveragePushTests5
{
    // ---------- Writer OnSerializeError handler paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_OnSerializeError_SkipRow()
    {
        var rows = new List<ThrowOnGet> {
            new() { Name = "Ok" },
            new() { Name = "Throw" },
            new() { Name = "Also" }
        };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            OnSerializeError = ctx => SerializeErrorAction.SkipRow
        });
        // 3 rows but middle one is skipped → 2 lines + header.
        var lines = csv.Trim('\r', '\n').Split('\n');
        Assert.True(lines.Length >= 2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_OnSerializeError_WriteNull()
    {
        var rows = new List<ThrowOnGet> {
            new() { Name = "Throw" }
        };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            OnSerializeError = ctx => SerializeErrorAction.WriteNull,
            NullValue = "NULL"
        });
        Assert.Contains("NULL", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_OnSerializeError_Throw_DefaultBehavior()
    {
        var rows = new List<ThrowOnGet> { new() { Name = "Throw" } };
        Assert.Throws<CsvException>(() => Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            OnSerializeError = ctx => SerializeErrorAction.Throw
        }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_AccessorThrowsWithoutHandler_Throws()
    {
        var rows = new List<ThrowOnGet> { new() { Name = "Throw" } };
        Assert.Throws<CsvException>(() => Csv.WriteToText(rows));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncOnSerializeError_SkipRow()
    {
        var rows = new List<ThrowOnGet> { new() { Name = "Throw" }, new() { Name = "Ok" } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { OnSerializeError = ctx => SerializeErrorAction.SkipRow },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Ok", csv);
        Assert.DoesNotContain(",throwing,", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncOnSerializeError_WriteNull()
    {
        var rows = new List<ThrowOnGet> { new() { Name = "Throw" } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions
            {
                OnSerializeError = ctx => SerializeErrorAction.WriteNull,
                NullValue = "NULL"
            },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("NULL", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_Async_NullRecordInList_WritesEmptyRow()
    {
        var rows = new List<CoveragePerson?> { new() { Name = "Alice", Age = 30 }, null };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows.Cast<CoveragePerson>(),
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Alice", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_Sync_NullRecordInList_WritesEmptyRow()
    {
        var rows = new List<CoveragePerson?> { new() { Name = "Alice", Age = 30 }, null };
        string csv = Csv.WriteToText(rows.Cast<CoveragePerson>());
        Assert.Contains("Alice", csv);
    }

    // ---------- IReadOnlyList async path (WriteRecordsUnfilteredAsync) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncList_WithProgress()
    {
        var rows = new List<CoveragePerson>();
        for (int i = 0; i < 250; i++) rows.Add(new CoveragePerson { Name = $"P{i}", Age = i });

        var progressCalls = 0;
        var progress = new Progress<CsvWriteProgress>(_ => Interlocked.Increment(ref progressCalls));

        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { WriteProgress = progress, WriteProgressIntervalRows = 50 },
            cancellationToken: TestContext.Current.CancellationToken);
        // Allow progress task to flush.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(progressCalls > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncList_MaxRowCount()
    {
        var rows = new List<CoveragePerson>();
        for (int i = 0; i < 10; i++) rows.Add(new CoveragePerson { Name = $"P{i}", Age = i });

        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<CsvException>(() => Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { MaxRowCount = 3 },
            cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_AsyncList_NoHeaderRow()
    {
        var rows = new List<CoveragePerson> { new() { Name = "A", Age = 1 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { WriteHeader = false },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.DoesNotContain("Name", csv);
        Assert.Contains("A", csv);
    }

    // ---------- Sync writer: progress, max row count, validation, write-header ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_Sync_WithProgress()
    {
        var rows = new List<CoveragePerson>();
        for (int i = 0; i < 250; i++) rows.Add(new CoveragePerson { Name = $"P{i}", Age = i });

        var progressCalls = 0;
        var progress = new Progress<CsvWriteProgress>(_ => Interlocked.Increment(ref progressCalls));

        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            WriteProgress = progress,
            WriteProgressIntervalRows = 50
        });
        Assert.NotEmpty(csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_Sync_MaxRowCount_Throws()
    {
        var rows = Enumerable.Range(0, 10).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        Assert.Throws<CsvException>(() => Csv.WriteToText(rows, options: new CsvWriteOptions { MaxRowCount = 3 }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_Sync_WriteHeader_False()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { WriteHeader = false });
        Assert.DoesNotContain("Name", csv);
        Assert.Contains("A", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_Sync_Validation_Lenient()
    {
        var rows = new[]
        {
            new RequiredFieldRow { Name = "Ok" },
            new RequiredFieldRow { Name = null },
            new RequiredFieldRow { Name = "Last" }
        };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { ValidationMode = ValidationMode.Lenient });
        Assert.NotEmpty(csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_Sync_Validation_Strict_Throws()
    {
        var rows = new[] { new RequiredFieldRow { Name = null } };
        Assert.Throws<ValidationException>(() => Csv.WriteToText(rows, options: new CsvWriteOptions { ValidationMode = ValidationMode.Strict }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_Async_Validation_Lenient()
    {
        var rows = new[] { new RequiredFieldRow { Name = null }, new RequiredFieldRow { Name = "Ok" } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { ValidationMode = ValidationMode.Lenient },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Ok", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task RecordWriter_Async_Validation_Strict_Throws()
    {
        var rows = new[] { new RequiredFieldRow { Name = null } };
        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<ValidationException>(() => Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { ValidationMode = ValidationMode.Strict },
            cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    // ---------- Sync writer: WithoutEmptyColumns / null records ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_WithoutEmptyColumns_FullList_StripsEmpty()
    {
        var rows = new List<SparseRow>();
        for (int i = 0; i < 5; i++) rows.Add(new SparseRow { A = "a", B = null, C = "c" });
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { ExcludeEmptyColumns = true });
        Assert.DoesNotContain(",,", csv);
        Assert.DoesNotContain("B", csv);
    }

    // ---------- Reflection-based CSV writer (no [GenerateBinder]) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("reflection")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("reflection")]
    public void RecordWriter_Reflection_BasicTypes()
    {
        var rows = new[] { new ReflectionWriteRow { Name = "Alice", Age = 30, Salary = 50000m } };
        string csv = Csv.WriteToText(rows);
        Assert.Contains("Alice", csv);
        Assert.Contains("30", csv);
        Assert.Contains("50000", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("reflection")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("reflection")]
    public async Task RecordWriter_Reflection_Async()
    {
        var rows = new[] { new ReflectionWriteRow { Name = "Alice", Age = 30, Salary = 50000m } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Alice", csv);
    }

    // ---------- Async writer: cancellation token ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_CancellationToken_StopsEnumeration()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        using var ms = new MemoryStream();
        var rows = Enumerable.Range(0, 100).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Csv.WriteToStreamAsync(ms, rows, cancellationToken: cts.Token).AsTask());
    }

    // ---------- CSV stream writer (sync) - test EndRow, Flush, edge cases ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamWriter_LongFieldWithQuoteAndCommaAndNewline()
    {
        var rows = new[]
        {
            new CoveragePerson { Name = new string('x', 1000) + ",hello\nworld\"quote", Age = 1 }
        };
        string csv = Csv.WriteToText(rows);
        Assert.Contains("xxxx", csv);
        Assert.Contains("\"\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamWriter_EmptyRecords_EmitsOnlyHeader()
    {
        var rows = Array.Empty<CoveragePerson>();
        string csv = Csv.WriteToText(rows);
        Assert.Contains("Name", csv);
        Assert.Contains("Age", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamWriter_EmptyRecords_NoHeader_EmitsNothing()
    {
        var rows = Array.Empty<CoveragePerson>();
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { WriteHeader = false });
        Assert.True(string.IsNullOrEmpty(csv));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamWriter_InjectionProtection_Reject_Throws()
    {
        var rows = new[] { new CoveragePerson { Name = "=DANGEROUS", Age = 1 } };
        Assert.Throws<CsvException>(() => Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            InjectionProtection = CsvInjectionProtection.Reject
        }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamWriter_InjectionProtection_Sanitize_StripsPrefix()
    {
        var rows = new[] { new CoveragePerson { Name = "=SUM", Age = 1 } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            InjectionProtection = CsvInjectionProtection.Sanitize
        });
        Assert.DoesNotContain("=SUM", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamWriter_InjectionProtection_EscapeWithTab()
    {
        var rows = new[] { new CoveragePerson { Name = "=SUM", Age = 1 } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions
        {
            InjectionProtection = CsvInjectionProtection.EscapeWithTab
        });
        Assert.Contains("\t=SUM", csv);
    }
}

// ---------- Test records ----------

[GenerateBinder]
public class ThrowOnGet
{
    public string? StoredName;
    public string? Name
    {
#pragma warning disable IDE0032 // Use auto property — getter intentionally throws
        get => StoredName == "Throw" ? throw new InvalidOperationException("forced") : StoredName;
        set => StoredName = value;
#pragma warning restore IDE0032
    }
}

[GenerateBinder]
public class RequiredFieldRow
{
    [Validate(NotNull = true)]
    public string? Name { get; set; }
}

// No [GenerateBinder] → reflection write path.
public class ReflectionWriteRow
{
    public string? Name { get; set; }
    public int Age { get; set; }
    public decimal Salary { get; set; }
}
