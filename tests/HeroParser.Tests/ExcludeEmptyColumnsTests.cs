using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests;

[Collection("AsyncWriterTests")]
public class ExcludeEmptyColumnsTests
{
    #region Test Record Types

    public class PersonRecord
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    public class NumericRecord
    {
        public string? Label { get; set; }
        public int Count { get; set; }
        public double? Score { get; set; }
    }

    public class EmptyToStringRecord
    {
        public string? Name { get; set; }
        public EmptyToStringType? Tag { get; set; }
    }

    /// <summary>
    /// A type whose ToString() returns empty string.
    /// </summary>
    public class EmptyToStringType
    {
        public override string ToString() => "";
    }

    /// <summary>
    /// Record with per-column ExcludeFromWriteIfAllEmpty on optional fields.
    /// </summary>
    public class ContactRecord
    {
        public string? Name { get; set; }

        [CsvColumn(ExcludeFromWriteIfAllEmpty = true)]
        public string? Phone { get; set; }

        [CsvColumn(ExcludeFromWriteIfAllEmpty = true)]
        public string? Fax { get; set; }
    }

    #endregion

    #region Core Behavior

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_SomeColumnsAllEmpty_ExcludesThoseColumns()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = "123" },
            new PersonRecord { Name = "Bob", Email = null, Phone = "456" },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        Assert.Equal("Name,Phone\r\nAlice,123\r\nBob,456\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_AllColumnsHaveData_OutputUnchanged()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = "a@b.com", Phone = "123" },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        Assert.Equal("Name,Email,Phone\r\nAlice,a@b.com,123\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_AllColumnsEmpty_WritesNothing()
    {
        var records = new[]
        {
            new PersonRecord { Name = null, Email = null, Phone = null },
            new PersonRecord { Name = "", Email = null, Phone = "" },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        Assert.Equal("", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_SingleRecord_MixedEmptyAndNonEmpty()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = "" },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        Assert.Equal("Name\r\nAlice\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_ColumnNonEmptyInAnyRecord_IsIncluded()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = null },
            new PersonRecord { Name = "Bob", Email = "b@b.com", Phone = null },
            new PersonRecord { Name = "Carol", Email = null, Phone = null },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        // Phone is excluded (all null), Email is included (non-empty in row 2)
        Assert.Equal("Name,Email\r\nAlice,\r\nBob,b@b.com\r\nCarol,\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_ZeroRecords_WritesHeaderWithAllColumns()
    {
        var records = Array.Empty<PersonRecord>();

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        Assert.Equal("Name,Email,Phone\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_Disabled_NoFiltering()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = null },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = false };
        var result = Csv.WriteToText(records, options);

        Assert.Equal("Name,Email,Phone\r\nAlice,,\r\n", result);
    }

    #endregion

    #region Empty Value Semantics

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_WhitespaceValue_TreatedAsNonEmpty()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = "  ", Phone = null },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        // Email has whitespace (non-empty), Phone is null (empty)
        Assert.Equal("Name,Email\r\nAlice,  \r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_NumericZero_TreatedAsNonEmpty()
    {
        var records = new[]
        {
            new NumericRecord { Label = "test", Count = 0, Score = null },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        // Count=0 is non-empty, Score=null is empty
        Assert.Equal("Label,Count\r\ntest,0\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_EmptyStringProperty_TreatedAsEmpty()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = "", Phone = "" },
            new PersonRecord { Name = "Bob", Email = "", Phone = "" },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        Assert.Equal("Name\r\nAlice\r\nBob\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_ToStringReturnsEmpty_TreatedAsEmpty()
    {
        var records = new[]
        {
            new EmptyToStringRecord { Name = "Alice", Tag = new EmptyToStringType() },
            new EmptyToStringRecord { Name = "Bob", Tag = new EmptyToStringType() },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        // Tag.ToString() returns "" for all records → column excluded
        Assert.Equal("Name\r\nAlice\r\nBob\r\n", result);
    }

    #endregion

    #region Option Integration

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_WithWriteHeaderFalse_DataRowsFiltered()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = "123" },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true, WriteHeader = false };
        var result = Csv.WriteToText(records, options);

        Assert.Equal("Alice,123\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_WithWriteHeaderFalse_AllColumnsEmpty_EmptyOutput()
    {
        var records = new[]
        {
            new PersonRecord { Name = null, Email = null, Phone = null },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true, WriteHeader = false };
        var result = Csv.WriteToText(records, options);

        Assert.Equal("", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_WithCustomNullValue_ScanUsesPreSerializationValues()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = null },
        };

        // NullValue="N/A" means null is written as "N/A", but the scan
        // still sees null (pre-serialization) → column is empty
        var options = new CsvWriteOptions { ExcludeEmptyColumns = true, NullValue = "N/A" };
        var result = Csv.WriteToText(records, options);

        // Email and Phone are excluded despite NullValue being "N/A"
        Assert.Equal("Name\r\nAlice\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_WithMaxRowCount_ThrowsDuringMaterialization()
    {
        var records = Enumerable.Range(0, 100).Select(i =>
            new PersonRecord { Name = $"Person{i}", Email = null, Phone = null });

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true, MaxRowCount = 10 };

        var ex = Assert.Throws<CsvException>(() => Csv.WriteToText(records, options));
        Assert.Contains("10", ex.Message);
    }

    #endregion

    #region Builder API

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithoutEmptyColumns_ExcludesEmptyColumns()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = "123" },
            new PersonRecord { Name = "Bob", Email = null, Phone = "456" },
        };

        var result = Csv.Write<PersonRecord>()
            .WithoutEmptyColumns()
            .ToText(records);

        Assert.Equal("Name,Phone\r\nAlice,123\r\nBob,456\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_WriteToFile_ExcludesEmptyColumns()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = "123" },
        };

        var path = Path.GetTempFileName();
        try
        {
            var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
            Csv.WriteToFile(path, records, options);
            var result = File.ReadAllText(path);
            Assert.Equal("Name,Phone\r\nAlice,123\r\n", result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion

    #region Async Paths

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ExcludeEmptyColumns_AsyncEnumerable_ExcludesEmptyColumns()
    {
        var records = ToAsyncEnumerable([
            new PersonRecord { Name = "Alice", Email = null, Phone = "123" },
            new PersonRecord { Name = "Bob", Email = null, Phone = "456" },
        ]);

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };

        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(stream, records, options, cancellationToken: TestContext.Current.CancellationToken);
        stream.Position = 0;
        var result = await new StreamReader(stream).ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Name,Phone\r\nAlice,123\r\nBob,456\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ExcludeEmptyColumns_AsyncWithSyncEnumerable_ExcludesEmptyColumns()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = "123" },
            new PersonRecord { Name = "Bob", Email = null, Phone = "456" },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };

        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(stream, records, options, cancellationToken: TestContext.Current.CancellationToken);
        stream.Position = 0;
        var result = await new StreamReader(stream).ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Name,Phone\r\nAlice,123\r\nBob,456\r\n", result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_NullRecordInCollection_TreatedAsAllEmpty()
    {
        var records = new PersonRecord?[]
        {
            null,
            new() { Name = "Alice", Email = null, Phone = null },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        // Only Name has non-empty value (from second record)
        Assert.Equal("Name\r\n\r\nAlice\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcludeEmptyColumns_ShortCircuit_AllColumnsNonEmptyInFirstRecord()
    {
        var records = new[]
        {
            new PersonRecord { Name = "Alice", Email = "a@b.com", Phone = "123" },
            new PersonRecord { Name = "Bob", Email = null, Phone = null },
        };

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        // All columns non-empty in first record → short-circuit, output includes all
        Assert.Equal("Name,Email,Phone\r\nAlice,a@b.com,123\r\nBob,,\r\n", result);
    }

    #endregion

    #region Per-Column ExcludeFromWriteIfAllEmpty

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void PerColumn_ExcludesMarkedColumnsWhenAllEmpty()
    {
        var records = new[]
        {
            new ContactRecord { Name = "Alice", Phone = null, Fax = null },
            new ContactRecord { Name = "Bob", Phone = "123", Fax = null },
        };

        // No global option — per-column attribute drives exclusion
        var result = Csv.WriteToText(records);

        // Fax excluded (all null, marked), Phone included (non-empty in row 2, marked)
        // Name always included (not marked)
        Assert.Equal("Name,Phone\r\nAlice,\r\nBob,123\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void PerColumn_UnmarkedColumnsAlwaysIncluded()
    {
        var records = new[]
        {
            new ContactRecord { Name = null, Phone = null, Fax = null },
        };

        // Name is NOT marked → always included even though all empty
        // Phone and Fax are marked → excluded
        var result = Csv.WriteToText(records);

        Assert.Equal("Name\r\n\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void PerColumn_AllMarkedColumnsHaveData_NoFiltering()
    {
        var records = new[]
        {
            new ContactRecord { Name = "Alice", Phone = "123", Fax = "456" },
        };

        var result = Csv.WriteToText(records);

        Assert.Equal("Name,Phone,Fax\r\nAlice,123,456\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void PerColumn_CombinedWithGlobalOption_BothApply()
    {
        var records = new[]
        {
            new ContactRecord { Name = null, Phone = null, Fax = null },
        };

        // Global option excludes Name (all empty), per-column excludes Phone and Fax
        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };
        var result = Csv.WriteToText(records, options);

        // All columns empty → empty output
        Assert.Equal("", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void PerColumn_ZeroRecords_HeaderIncludesAllColumns()
    {
        var records = Array.Empty<ContactRecord>();

        var result = Csv.WriteToText(records);

        // Zero records → no scan data → all columns in header
        Assert.Equal("Name,Phone,Fax\r\n", result);
    }

    #endregion

    #region Helpers

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }

    #endregion
}
