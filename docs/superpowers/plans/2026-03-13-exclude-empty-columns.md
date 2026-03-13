# ExcludeEmptyColumns Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `CsvWriteOptions.ExcludeEmptyColumns` option that omits columns from CSV output when all records have empty values for that column.

**Architecture:** Single-pass scan with `bool[]` bitmask + short-circuit, then filtered write. Logic lives in `CsvRecordWriter<T>` which is shared by all record-based paths (reflection, source-generated, fluent map). Records are materialized to `List<T>` when the option is enabled.

**Tech Stack:** C# (.NET 8/9/10), xUnit v3, no new dependencies

**Spec:** `docs/superpowers/specs/2026-03-13-exclude-empty-columns-design.md`

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `src/HeroParser/SeparatedValues/Writing/CsvWriteOptions.cs` | Modify | Add `ExcludeEmptyColumns` property |
| `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs` | Modify | Add scan+filter logic, helper methods, filtered write paths |
| `src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs` | Modify | Add `WithoutEmptyColumns()` to `CsvWriterBuilder<T>` |
| `tests/HeroParser.Tests/ExcludeEmptyColumnsTests.cs` | Create | All tests for this feature |

---

## Task 1: Add `ExcludeEmptyColumns` property to `CsvWriteOptions`

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Writing/CsvWriteOptions.cs`

- [ ] **Step 1: Add `ExcludeEmptyColumns` property**

In `CsvWriteOptions.cs`, add after the `WriteHeader` property (around line 112):

```csharp
/// <summary>
/// Gets or sets a value indicating whether to exclude columns where every record's value is empty.
/// </summary>
/// <remarks>
/// <para>When <see langword="true"/>, columns where all records have <see langword="null"/> or empty string
/// values are omitted from the output (both header and data rows). Whitespace values are <b>not</b>
/// considered empty.</para>
/// <para>Requires materializing all records before writing to determine which columns are empty.
/// Not suitable for unbounded streaming scenarios.</para>
/// <para>The emptiness check inspects pre-serialization values from property accessors,
/// not the formatted output. The <see cref="NullValue"/> setting does not affect which columns
/// are considered empty.</para>
/// </remarks>
public bool ExcludeEmptyColumns { get; init; } = false;
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build src/HeroParser`
Expected: Build succeeds with 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/HeroParser/SeparatedValues/Writing/CsvWriteOptions.cs
git commit -m "feat: Add ExcludeEmptyColumns property to CsvWriteOptions"
```

---

## Task 2: Add `WithoutEmptyColumns()` to `CsvWriterBuilder<T>`

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs`

- [ ] **Step 1: Add field and method to `CsvWriterBuilder<T>`**

In `CsvWriterBuilder<T>`, add a private field alongside the other fields (around line 18):

```csharp
private bool excludeEmptyColumns;
```

Add the fluent method after `WithoutHeader()` (around line 137):

```csharp
/// <summary>
/// Excludes columns from the output where all records have empty (null or "") values.
/// Whitespace values are not considered empty.
/// </summary>
/// <returns>This builder for method chaining.</returns>
/// <remarks>
/// Requires materializing all records before writing to determine which columns are empty.
/// Not suitable for unbounded streaming scenarios.
/// </remarks>
public CsvWriterBuilder<T> WithoutEmptyColumns()
{
    excludeEmptyColumns = true;
    cachedOptions = null;
    return this;
}
```

- [ ] **Step 2: Wire into `GetOptions()`**

In the `GetOptions()` method of `CsvWriterBuilder<T>`, add `ExcludeEmptyColumns = excludeEmptyColumns` to the options initializer (alongside the other properties like `WriteHeader = writeHeader`).

- [ ] **Step 3: Verify build succeeds**

Run: `dotnet build src/HeroParser`
Expected: Build succeeds with 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs
git commit -m "feat: Add WithoutEmptyColumns() to CsvWriterBuilder<T>"
```

---

## Task 3: Write core tests (TDD — tests first)

**Files:**
- Create: `tests/HeroParser.Tests/ExcludeEmptyColumnsTests.cs`

- [ ] **Step 1: Create test file with record types and core behavior tests**

Create `tests/HeroParser.Tests/ExcludeEmptyColumnsTests.cs`:

```csharp
using HeroParser.SeparatedValues.Core;
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
        Assert.Equal("Name,Email\r\nAlice,\"  \"\r\n", result);
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
        var records = ToAsyncEnumerable(new[]
        {
            new PersonRecord { Name = "Alice", Email = null, Phone = "123" },
            new PersonRecord { Name = "Bob", Email = null, Phone = "456" },
        });

        var options = new CsvWriteOptions { ExcludeEmptyColumns = true };

        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(stream, records, options);
        stream.Position = 0;
        var result = new StreamReader(stream).ReadToEnd();

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
        await Csv.WriteToStreamAsync<PersonRecord>(stream, records, options);
        stream.Position = 0;
        var result = new StreamReader(stream).ReadToEnd();

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
            new PersonRecord { Name = "Alice", Email = null, Phone = null },
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~ExcludeEmptyColumnsTests" -v n`
Expected: All tests FAIL (feature not implemented yet)

- [ ] **Step 3: Commit test file**

```bash
git add tests/HeroParser.Tests/ExcludeEmptyColumnsTests.cs
git commit -m "test: Add ExcludeEmptyColumns tests (red)"
```

---

## Task 4: Implement scan+filter logic in `CsvRecordWriter<T>`

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs`

This is the core implementation. Add helper methods and modify all 4 write methods.

- [ ] **Step 1: Add `IsValueEmpty` static helper method**

Add after the `WriteHeaderRow` private method (around line 475):

```csharp
private static bool IsValueEmpty(object? value)
{
    if (value is null)
        return true;
    if (value is string s)
        return s.Length == 0;
    var str = value.ToString();
    return str is null or { Length: 0 };
}
```

- [ ] **Step 2: Add `ScanForNonEmptyColumns` method**

Add after `IsValueEmpty`:

```csharp
private int[] ScanForNonEmptyColumns(IReadOnlyList<T> records)
{
    int columnCount = accessors.Length;
    var hasNonEmpty = new bool[columnCount];
    int nonEmptyCount = 0;

    foreach (var record in records)
    {
        if (record is null)
            continue;

        for (int i = 0; i < columnCount; i++)
        {
            if (hasNonEmpty[i])
                continue;

            object? value;
            try
            {
                value = accessors[i].GetValue(record);
            }
            catch
            {
                // Failed getter → treat as empty for scan purposes
                continue;
            }

            if (!IsValueEmpty(value))
            {
                hasNonEmpty[i] = true;
                nonEmptyCount++;
                if (nonEmptyCount == columnCount)
                    return []; // All columns non-empty → no filtering needed
            }
        }
    }

    // Build filtered index array
    if (nonEmptyCount == 0)
        return [-1]; // Sentinel: all columns empty

    var indices = new int[nonEmptyCount];
    int idx = 0;
    for (int i = 0; i < columnCount; i++)
    {
        if (hasNonEmpty[i])
            indices[idx++] = i;
    }
    return indices;
}
```

The return value conventions:
- Empty array (`[]`) → no filtering needed (all columns have data)
- `[-1]` sentinel → all columns empty, write nothing
- Otherwise → filtered column indices

- [ ] **Step 3: Add `WriteFilteredHeader` and `WriteFilteredRecord` helper methods**

```csharp
private void WriteFilteredHeader(CsvStreamWriter writer, int[] columnIndices)
{
    var filteredHeaders = new string[columnIndices.Length];
    for (int i = 0; i < columnIndices.Length; i++)
        filteredHeaders[i] = headerBuffer[columnIndices[i]];
    writer.WriteRow(filteredHeaders);
}

private void WriteFilteredRecord(CsvStreamWriter writer, T record, int rowNumber, int[] columnIndices)
{
    if (record is null)
    {
        writer.EndRow();
        return;
    }

    var filteredValues = new object?[columnIndices.Length];
    var filteredFormats = new string?[columnIndices.Length];
    for (int i = 0; i < columnIndices.Length; i++)
    {
        int ci = columnIndices[i];
        filteredFormats[i] = formatsBuffer[ci];
        try
        {
            filteredValues[i] = accessors[ci].GetValue(record);
        }
        catch (Exception ex)
        {
            if (writerOptions.OnSerializeError is not null)
            {
                var context = new CsvSerializeErrorContext
                {
                    Row = rowNumber,
                    Column = ci + 1,
                    MemberName = accessors[ci].MemberName,
                    SourceType = typeof(T),
                    Value = null,
                    Exception = ex
                };

                var action = writerOptions.OnSerializeError(context);
                switch (action)
                {
                    case SerializeErrorAction.SkipRow:
                        return;
                    case SerializeErrorAction.WriteNull:
                        filteredValues[i] = null;
                        continue;
                    case SerializeErrorAction.Throw:
                    default:
                        break;
                }
            }

            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row {rowNumber}, Column {ci + 1}: Failed to get value for member '{accessors[ci].MemberName}': {ex.Message}",
                ex);
        }
    }
    writer.WriteRowWithFormats(filteredValues, filteredFormats);
}
```

- [ ] **Step 4: Add async variant of `WriteFilteredRecord`**

```csharp
private async ValueTask WriteFilteredRecordAsync(CsvAsyncStreamWriter writer, T record, int rowNumber, int[] columnIndices, CancellationToken cancellationToken)
{
    if (record is null)
    {
        await writer.EndRowAsync(cancellationToken).ConfigureAwait(false);
        return;
    }

    var filteredValues = new object?[columnIndices.Length];
    var filteredFormats = new string?[columnIndices.Length];
    for (int i = 0; i < columnIndices.Length; i++)
    {
        int ci = columnIndices[i];
        filteredFormats[i] = formatsBuffer[ci];
        try
        {
            filteredValues[i] = accessors[ci].GetValue(record);
        }
        catch (Exception ex)
        {
            if (writerOptions.OnSerializeError is not null)
            {
                var context = new CsvSerializeErrorContext
                {
                    Row = rowNumber,
                    Column = ci + 1,
                    MemberName = accessors[ci].MemberName,
                    SourceType = typeof(T),
                    Value = null,
                    Exception = ex
                };

                var action = writerOptions.OnSerializeError(context);
                switch (action)
                {
                    case SerializeErrorAction.SkipRow:
                        return;
                    case SerializeErrorAction.WriteNull:
                        filteredValues[i] = null;
                        continue;
                    case SerializeErrorAction.Throw:
                    default:
                        break;
                }
            }

            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row {rowNumber}, Column {ci + 1}: Failed to get value for member '{accessors[ci].MemberName}': {ex.Message}",
                ex);
        }
    }
    await writer.WriteRowWithFormatsAsync(filteredValues, filteredFormats, cancellationToken).ConfigureAwait(false);
}

private async ValueTask WriteFilteredHeaderAsync(CsvAsyncStreamWriter writer, int[] columnIndices, CancellationToken cancellationToken)
{
    var filteredHeaders = new string[columnIndices.Length];
    for (int i = 0; i < columnIndices.Length; i++)
        filteredHeaders[i] = headerBuffer[columnIndices[i]];
    await writer.WriteRowAsync(filteredHeaders, cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 5: Add `MaterializeRecords` helper**

```csharp
private static IReadOnlyList<T> MaterializeRecords(IEnumerable<T> records, int? maxRowCount)
{
    if (records is IReadOnlyList<T> list)
    {
        if (maxRowCount.HasValue && list.Count > maxRowCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"Exceeded maximum row count of {maxRowCount.Value}");
        }
        return list;
    }

    var materialized = new List<T>();
    foreach (var record in records)
    {
        materialized.Add(record);
        if (maxRowCount.HasValue && materialized.Count > maxRowCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"Exceeded maximum row count of {maxRowCount.Value}");
        }
    }
    return materialized;
}

private static async ValueTask<IReadOnlyList<T>> MaterializeRecordsAsync(IAsyncEnumerable<T> records, int? maxRowCount, CancellationToken cancellationToken)
{
    var materialized = new List<T>();
    await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
    {
        materialized.Add(record);
        if (maxRowCount.HasValue && materialized.Count > maxRowCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"Exceeded maximum row count of {maxRowCount.Value}");
        }
    }
    return materialized;
}
```

- [ ] **Step 6: Add `WriteRecordsFiltered` private method (sync path)**

This is a self-contained method that handles the full filtered write flow for a materialized list. Called from `WriteRecords` when `ExcludeEmptyColumns` is true.

```csharp
private void WriteRecordsFiltered(CsvStreamWriter writer, IReadOnlyList<T> records, bool includeHeader)
{
    var columnIndices = ScanForNonEmptyColumns(records);

    // All columns non-empty → no filtering, use normal path
    if (columnIndices.Length == 0)
    {
        WriteRecordsUnfiltered(writer, records, includeHeader);
        return;
    }

    // All columns empty → write nothing
    if (columnIndices is [-1])
        return;

    var progress = writerOptions.WriteProgress;
    var progressInterval = writerOptions.WriteProgressIntervalRows;
    int rowNumber = 0;

    if (includeHeader && writerOptions.WriteHeader)
    {
        WriteFilteredHeader(writer, columnIndices);
        rowNumber++;
    }

    for (int r = 0; r < records.Count; r++)
    {
        rowNumber++;
        WriteFilteredRecord(writer, records[r], rowNumber, columnIndices);

        if (progress is not null && (r + 1) % progressInterval == 0)
        {
            progress.Report(new CsvWriteProgress
            {
                RowsWritten = r + 1,
                BytesWritten = writer.CharsWritten,
            });
        }
    }

    progress?.Report(new CsvWriteProgress
    {
        RowsWritten = records.Count,
        BytesWritten = writer.CharsWritten,
    });
}
```

- [ ] **Step 7: Add `WriteRecordsUnfiltered` private method**

Extract the existing loop logic from `WriteRecords` into a reusable method so the filtered path can call it when short-circuiting:

```csharp
private void WriteRecordsUnfiltered(CsvStreamWriter writer, IReadOnlyList<T> records, bool includeHeader)
{
    int rowNumber = 0;
    var maxRows = writerOptions.MaxRowCount;
    var progress = writerOptions.WriteProgress;
    var progressInterval = writerOptions.WriteProgressIntervalRows;

    if (includeHeader && writerOptions.WriteHeader)
    {
        WriteHeaderRow(writer);
        rowNumber++;
    }

    for (int r = 0; r < records.Count; r++)
    {
        rowNumber++;

        if (maxRows.HasValue && (r + 1) > maxRows.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"Exceeded maximum row count of {maxRows.Value}");
        }

        WriteRecordInternal(writer, records[r], rowNumber);

        if (progress is not null && (r + 1) % progressInterval == 0)
        {
            progress.Report(new CsvWriteProgress
            {
                RowsWritten = r + 1,
                BytesWritten = writer.CharsWritten,
            });
        }
    }

    progress?.Report(new CsvWriteProgress
    {
        RowsWritten = records.Count,
        BytesWritten = writer.CharsWritten,
    });
}
```

- [ ] **Step 8: Modify `WriteRecords` to branch on `ExcludeEmptyColumns`**

Replace the body of the existing `WriteRecords` method with:

```csharp
public void WriteRecords(CsvStreamWriter writer, IEnumerable<T> records, bool includeHeader = true)
{
    if (writerOptions.ExcludeEmptyColumns)
    {
        var materialized = MaterializeRecords(records, writerOptions.MaxRowCount);
        WriteRecordsFiltered(writer, materialized, includeHeader);
        return;
    }

    // Existing implementation unchanged below...
    int rowNumber = 0;
    int dataRowCount = 0;
    // ... (keep all existing code)
}
```

- [ ] **Step 9: Run core tests to verify they pass**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~ExcludeEmptyColumnsTests" -v n`
Expected: All sync tests PASS. Async tests may still fail (async paths not modified yet).

- [ ] **Step 10: Commit**

```bash
git add src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs
git commit -m "feat: Implement ExcludeEmptyColumns scan+filter in sync WriteRecords path"
```

---

## Task 5: Implement async write paths

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs`

- [ ] **Step 1: Add `WriteRecordsFilteredAsync` for `CsvAsyncStreamWriter`**

Note: There is no separate `WriteRecordsFilteredAsync` overload for `CsvStreamWriter` because the scan+write logic is fully synchronous. The `WriteRecordsAsync(CsvStreamWriter, IAsyncEnumerable<T>, ...)` overload calls `WriteRecordsFiltered` (sync) directly after materializing.

```csharp
private async ValueTask WriteRecordsFilteredAsync(
    CsvAsyncStreamWriter writer,
    IReadOnlyList<T> records,
    bool includeHeader,
    CancellationToken cancellationToken)
{
    var columnIndices = ScanForNonEmptyColumns(records);

    if (columnIndices.Length == 0)
    {
        // All columns non-empty → delegate to existing unfiltered async path
        // Re-enumerate the materialized list through the existing method
        await WriteRecordsUnfilteredAsync(writer, records, includeHeader, cancellationToken).ConfigureAwait(false);
        return;
    }

    if (columnIndices is [-1])
        return;

    var progress = writerOptions.WriteProgress;
    var progressInterval = writerOptions.WriteProgressIntervalRows;
    int rowNumber = 0;

    if (includeHeader && writerOptions.WriteHeader)
    {
        await WriteFilteredHeaderAsync(writer, columnIndices, cancellationToken).ConfigureAwait(false);
        rowNumber++;
    }

    for (int r = 0; r < records.Count; r++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        rowNumber++;
        await WriteFilteredRecordAsync(writer, records[r], rowNumber, columnIndices, cancellationToken).ConfigureAwait(false);

        if (progress is not null && (r + 1) % progressInterval == 0)
        {
            progress.Report(new CsvWriteProgress
            {
                RowsWritten = r + 1,
                BytesWritten = writer.CharsWritten,
            });
        }
    }

    progress?.Report(new CsvWriteProgress
    {
        RowsWritten = records.Count,
        BytesWritten = writer.CharsWritten,
    });
}
```

- [ ] **Step 3: Add `WriteRecordsUnfilteredAsync` helper for `CsvAsyncStreamWriter`**

```csharp
private async ValueTask WriteRecordsUnfilteredAsync(
    CsvAsyncStreamWriter writer,
    IReadOnlyList<T> records,
    bool includeHeader,
    CancellationToken cancellationToken)
{
    int rowNumber = 0;
    var maxRows = writerOptions.MaxRowCount;
    var progress = writerOptions.WriteProgress;
    var progressInterval = writerOptions.WriteProgressIntervalRows;

    if (includeHeader && writerOptions.WriteHeader)
    {
        await writer.WriteRowAsync(headerBuffer, cancellationToken).ConfigureAwait(false);
        rowNumber++;
    }

    for (int r = 0; r < records.Count; r++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        rowNumber++;

        if (maxRows.HasValue && (r + 1) > maxRows.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"Exceeded maximum row count of {maxRows.Value}");
        }

        await WriteRecordInternalAsync(writer, records[r], rowNumber, cancellationToken).ConfigureAwait(false);

        if (progress is not null && (r + 1) % progressInterval == 0)
        {
            progress.Report(new CsvWriteProgress
            {
                RowsWritten = r + 1,
                BytesWritten = writer.CharsWritten,
            });
        }
    }

    progress?.Report(new CsvWriteProgress
    {
        RowsWritten = records.Count,
        BytesWritten = writer.CharsWritten,
    });
}
```

- [ ] **Step 4: Modify all 3 `WriteRecordsAsync` overloads**

For each of the 3 async `WriteRecordsAsync` methods, add an `ExcludeEmptyColumns` branch at the top, similar to what was done in `WriteRecords`:

**Overload 1** — `WriteRecordsAsync(CsvStreamWriter, IAsyncEnumerable<T>, ...)`:
```csharp
if (writerOptions.ExcludeEmptyColumns)
{
    var materialized = await MaterializeRecordsAsync(records, writerOptions.MaxRowCount, cancellationToken).ConfigureAwait(false);
    WriteRecordsFiltered(writer, materialized, includeHeader);
    return;
}
```

**Overload 2** — `WriteRecordsAsync(CsvAsyncStreamWriter, IAsyncEnumerable<T>, ...)`:
```csharp
if (writerOptions.ExcludeEmptyColumns)
{
    var materialized = await MaterializeRecordsAsync(records, writerOptions.MaxRowCount, cancellationToken).ConfigureAwait(false);
    await WriteRecordsFilteredAsync(writer, materialized, includeHeader, cancellationToken).ConfigureAwait(false);
    return;
}
```

**Overload 3** — `WriteRecordsAsync(CsvAsyncStreamWriter, IEnumerable<T>, ...)`:
```csharp
if (writerOptions.ExcludeEmptyColumns)
{
    var materialized = MaterializeRecords(records, writerOptions.MaxRowCount);
    await WriteRecordsFilteredAsync(writer, materialized, includeHeader, cancellationToken).ConfigureAwait(false);
    return;
}
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~ExcludeEmptyColumnsTests" -v n`
Expected: ALL tests PASS (including async tests)

- [ ] **Step 6: Run full test suite to verify no regressions**

Run: `dotnet test tests/HeroParser.Tests -v n`
Expected: All existing tests PASS

- [ ] **Step 7: Commit**

```bash
git add src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs
git commit -m "feat: Implement ExcludeEmptyColumns in all async WriteRecordsAsync paths"
```

---

## Task 6: Verify formatting and final validation

- [ ] **Step 1: Run format check**

Run: `dotnet format --verify-no-changes`
Expected: PASS. If it fails, run `dotnet format` and commit the fixes.

- [ ] **Step 2: Run full build across all frameworks**

Run: `dotnet build -c Release`
Expected: Build succeeds with 0 errors, 0 warnings

- [ ] **Step 3: Run full test suite**

Run: `dotnet test -c Release`
Expected: All tests pass

- [ ] **Step 4: Final commit (if any format fixes)**

```bash
git add -A
git commit -m "style: Fix formatting for ExcludeEmptyColumns implementation"
```
