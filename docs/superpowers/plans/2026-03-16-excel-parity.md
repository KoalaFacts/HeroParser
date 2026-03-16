# Excel Read Parity Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the critical API gaps between Excel reading and CSV/FixedWidth: validation enforcement, `NullValues`, `CaseSensitiveHeaders`, `AllowMissingColumns`, and `ExcelReadOptions` class.

**Architecture:** Excel already reuses the CSV binder infrastructure (`CsvRecordBinderFactory.GetCharBinder<T>`). The gaps exist because Excel passes no `CsvRecordOptions` and never collects validation errors. The fix: create `ExcelReadOptions` to mirror `CsvRecordOptions`, pass it through the binder, collect errors during binding, and enforce `ValidationMode`.

**Tech Stack:** C#, existing `CsvRecordOptions` / `ICsvBinder` / `ValidationError` infrastructure.

---

## File Structure

### New Files
- `src/HeroParser/Excels/Core/ExcelReadOptions.cs` — Options record mirroring the relevant subset of `CsvRecordOptions` for Excel context
- `tests/HeroParser.Tests/Excel/ExcelValidationTests.cs` — Tests for validation enforcement during Excel reading

### Modified Files
- `src/HeroParser/Excels/Reading/ExcelRecordReaderBuilder.cs` — Pass options to binder, collect errors, enforce validation mode
- `src/HeroParser/Excels/Reading/ExcelAllSheetsBuilder.cs` — Same changes for all-sheets path
- `src/HeroParser/Excels/Reading/ExcelMultiSheetBuilder.cs` — Same changes for multi-sheet path

---

## Chunk 1: ExcelReadOptions + Validation Enforcement

### Task 1: Create ExcelReadOptions

**Files:**
- Create: `src/HeroParser/Excels/Core/ExcelReadOptions.cs`

- [ ] **Step 1: Create the options record**

```csharp
using System.Globalization;
using HeroParser.Validation;

namespace HeroParser.Excels.Core;

/// <summary>
/// Configuration options for Excel record reading.
/// </summary>
public sealed record ExcelReadOptions
{
    /// <summary>Gets or sets whether the first data row is a header row. Default is <see langword="true"/>.</summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>Gets or sets whether header matching is case-sensitive. Default is <see langword="false"/>.</summary>
    public bool CaseSensitiveHeaders { get; init; } = false;

    /// <summary>Gets or sets whether missing columns are tolerated. Default is <see langword="false"/>.</summary>
    public bool AllowMissingColumns { get; init; } = false;

    /// <summary>Gets or sets string values that should be treated as null during parsing.</summary>
    public IReadOnlyList<string>? NullValues { get; init; }

    /// <summary>Gets or sets the culture for parsing cell values. Default is <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>Gets or sets the maximum number of data rows to read. Null means no limit.</summary>
    public int? MaxRows { get; init; }

    /// <summary>Gets or sets the number of rows to skip before reading data.</summary>
    public int SkipRows { get; init; }

    /// <summary>Gets or sets the validation mode. Default is <see cref="ValidationMode.Strict"/>.</summary>
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Strict;

    /// <summary>Gets a reusable default instance.</summary>
    public static ExcelReadOptions Default { get; } = new();
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build`

- [ ] **Step 3: Commit**

### Task 2: Wire ExcelReadOptions into ExcelRecordReaderBuilder

**Files:**
- Modify: `src/HeroParser/Excels/Reading/ExcelRecordReaderBuilder.cs`

The key changes:
1. Replace individual fields with `ExcelReadOptions` construction
2. Build a `CsvRecordOptions` from `ExcelReadOptions` to pass to `GetCharBinder`
3. In `ReadRecords()`, pass an errors list to `binder.TryBind()`
4. After reading all records, enforce `ValidationMode` (throw in Strict if errors)

- [ ] **Step 1: Write failing test for validation enforcement**

Create `tests/HeroParser.Tests/Excel/ExcelValidationTests.cs`:

```csharp
using HeroParser.Excels.Reading;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelValidationTests
{
    // Uses ValidatedTransaction from existing test records
    // which has [Validate(NotNull = true)] on decimal Amount

    [Fact]
    public void StrictMode_WithValidationErrors_ThrowsOnFromFile()
    {
        // Create an xlsx with an empty Amount cell
        // This test verifies that validation errors are enforced
        // Will be implemented once we have the test helper
    }
}
```

- [ ] **Step 2: Update ReadRecords to collect and enforce validation errors**

In `ExcelRecordReaderBuilder.ReadRecords()`, change line 163 from:
```csharp
var binder = CsvRecordBinderFactory.GetCharBinder<T>(delimiter: '\x01');
```
to:
```csharp
var recordOptions = new CsvRecordOptions
{
    HasHeaderRow = hasHeaderRow,
    CaseSensitiveHeaders = caseSensitiveHeaders,
    AllowMissingColumns = allowMissingColumns,
    NullValues = nullValues,
    Culture = culture,
    ValidationMode = validationMode
};
var binder = CsvRecordBinderFactory.GetCharBinder<T>(recordOptions, delimiter: '\x01');
```

And change the binding loop (line 198) from:
```csharp
if (binder.TryBind(csvRow, sheetReader.CurrentRowNumber, out var record))
{
    results.Add(record);
}
```
to:
```csharp
if (binder.TryBind(csvRow, sheetReader.CurrentRowNumber, out var record, errors))
{
    results.Add(record);
}
```

Where `errors` is a `List<ValidationError>` created before the loop.

After the loop, enforce validation mode:
```csharp
if (validationMode == ValidationMode.Strict && errors.Count > 0)
    throw new ValidationException(errors);
```

- [ ] **Step 3: Add missing builder fields and methods**

Add these fields to `ExcelRecordReaderBuilder<T>`:
```csharp
private bool caseSensitiveHeaders;
private bool allowMissingColumns;
private IReadOnlyList<string>? nullValues;
```

Add builder methods:
```csharp
public ExcelRecordReaderBuilder<T> CaseSensitiveHeaders() { caseSensitiveHeaders = true; return this; }
public ExcelRecordReaderBuilder<T> AllowMissingColumns() { allowMissingColumns = true; return this; }
public ExcelRecordReaderBuilder<T> WithNullValues(params string[] values) { nullValues = values; return this; }
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet build && dotnet test tests/HeroParser.Tests -f net10.0`

- [ ] **Step 5: Commit**

### Task 3: Wire validation into ExcelAllSheetsBuilder

**Files:**
- Modify: `src/HeroParser/Excels/Reading/ExcelAllSheetsBuilder.cs`

Same pattern: pass `CsvRecordOptions` with null values / case sensitivity / allow missing / validation mode to the char binder, collect errors, enforce mode.

- [ ] **Step 1: Update constructor to accept new fields**
- [ ] **Step 2: Pass options through ConfigureBuilder**
- [ ] **Step 3: Build and verify**
- [ ] **Step 4: Commit**

### Task 4: Wire validation into ExcelMultiSheetBuilder

**Files:**
- Modify: `src/HeroParser/Excels/Reading/ExcelMultiSheetBuilder.cs`

Same pattern for the multi-sheet reading path.

- [ ] **Step 1: Apply same changes**
- [ ] **Step 2: Build and verify**
- [ ] **Step 3: Commit**

---

## Chunk 2: Integration Tests

### Task 5: Write Excel validation integration tests

**Files:**
- Create: `tests/HeroParser.Tests/Excel/ExcelValidationTests.cs`

Tests to write:
1. **Strict mode with NotNull violation** — empty decimal cell → throws `ValidationException`
2. **Strict mode with valid data** — no errors, returns all records
3. **Lenient mode with NotNull violation** — skips invalid rows, returns valid ones, no throw
4. **NullValues support** — configure null values, verify they're treated as null
5. **CaseSensitiveHeaders** — mismatched case header → column not found
6. **AllowMissingColumns** — missing column doesn't throw

Use the existing `ExcelTestHelper` for creating test xlsx data in-memory.

- [ ] **Step 1: Write all tests**
- [ ] **Step 2: Run and verify**
- [ ] **Step 3: Commit**

---

## Chunk 3: Static Facade + Final Verification

### Task 6: Update Excel.Read.cs facade methods

**Files:**
- Modify: `src/HeroParser/Excel.Read.cs`

The static `Excel.Read<T>()` entry point should propagate options. Check if `DeserializeRecords` needs updates.

- [ ] **Step 1: Review and update static methods**
- [ ] **Step 2: Build and verify**

### Task 7: Full test suite + format check

- [ ] **Step 1: Run all tests**

```bash
dotnet test -f net10.0
dotnet format --verify-no-changes
```

- [ ] **Step 2: Commit**
