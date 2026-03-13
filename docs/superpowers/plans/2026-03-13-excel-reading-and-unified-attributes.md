# Excel Reading & Unified Attribute System — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Excel (.xlsx) reading to HeroParser and unify the attribute system across CSV, FixedWidth, and Excel formats as a v2 breaking change.

**Architecture:** Two interconnected workstreams executed sequentially. First, create unified attributes (TabularMap, PositionalMap, Parse, Validate, Format, GenerateBinder) and migrate all existing code. Then build Excel .xlsx reading on top of the new foundation using `System.IO.Compression` + `System.Xml.XmlReader` with zero new dependencies.

**Tech Stack:** C# (.NET 8/9/10), Roslyn Source Generators (netstandard2.0), System.IO.Compression, System.Xml, xUnit.v3, BenchmarkDotNet

**Spec:** `docs/superpowers/specs/2026-03-13-excel-reading-and-unified-attributes-design.md`

---

## File Map

### New files to create

```
src/HeroParser/
  Attributes/
    TabularMapAttribute.cs              # Column-based mapping (CSV, Excel)
    PositionalMapAttribute.cs           # Position-based mapping (FixedWidth)
    ParseAttribute.cs                   # Read-side type conversion
    ValidateAttribute.cs                # Bidirectional validation constraints
    FormatAttribute.cs                  # Write-side serialization
    GenerateBinderAttribute.cs          # Source generator trigger
    FieldAlignment.cs                   # Moved from FixedWidths/ namespace

  Excel.cs                              # Static partial class entry point
  Excel.Read.cs                         # Read factory methods
  Excel.DataReader.cs                   # IDataReader factory methods

  Excel/
    Core/
      ExcelReadOptions.cs               # Options record
      ExcelException.cs                 # Excel-specific errors
      ExcelProgress.cs                  # Progress reporting
    Reading/
      ExcelRecordReaderBuilder.cs       # Fluent builder mirroring CSV
      ExcelRowReaderBuilder.cs          # Row-level builder
      ExcelAllSheetsBuilder.cs          # AllSheets() same-type
      ExcelMultiSheetBuilder.cs         # WithSheet<T>() multi-type
      ExcelMultiSheetResult.cs          # Result container
      Data/
        ExcelDataReader.cs              # DbDataReader implementation
    Xlsx/
      XlsxReader.cs                     # Orchestrator (owns ZipArchive)
      XlsxWorkbook.cs                   # workbook.xml parser
      XlsxSharedStrings.cs             # sharedStrings.xml parser
      XlsxStylesheet.cs                # styles.xml date detection
      XlsxSheetReader.cs               # Streaming sheet row reader
      XlsxCellType.cs                  # Cell type enum
      XlsxRowAdapter.cs               # Excel-to-CsvRow<char> bridge

tests/HeroParser.Tests/
  Excel/
    XlsxSharedStringsTests.cs
    XlsxStylesheetTests.cs
    XlsxWorkbookTests.cs
    XlsxSheetReaderTests.cs
    ExcelReadTests.cs
    ExcelSheetSelectionTests.cs
    ExcelMultiSheetTests.cs
    ExcelDataReaderTests.cs
    ExcelEdgeCaseTests.cs
  Attributes/
    AttributeMigrationTests.cs          # Verifies new attributes work end-to-end
  Fixtures/Excel/
    ExcelTestHelper.cs                  # Programmatic .xlsx creation for tests
```

### Files to delete

```
src/HeroParser/SeparatedValues/Reading/Shared/CsvColumnAttribute.cs
src/HeroParser/SeparatedValues/Reading/Shared/CsvGenerateBinderAttribute.cs
src/HeroParser/FixedWidths/Records/Binding/FixedWidthColumnAttribute.cs
src/HeroParser/FixedWidths/Records/Binding/FixedWidthGenerateBinderAttribute.cs
src/HeroParser/FixedWidths/FieldAlignment.cs                              # Moved to Attributes/
```

### Files to modify (source — 28 files)

```
src/HeroParser.Generators/CsvRecordBinderGenerator.cs                     # FQN strings, attribute property reading
src/HeroParser.Generators/FixedWidthRecordBinderGenerator.cs              # FQN strings, attribute property reading
src/HeroParser.Generators/CsvMultiSchemaDispatcherGenerator.cs            # FQN for GenerateBinder search
src/HeroParser.Generators/GeneratorHelpers.cs                             # Diagnostic messages

src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs                 # ExcludeFromWriteIfAllEmpty → FormatAttribute
src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs                # Attribute references
src/HeroParser/SeparatedValues/Reading/Binders/CsvRecordBinderFactory.cs  # Error message references
src/HeroParser/SeparatedValues/Reading/Records/CsvRecordReaderBuilder.cs  # Attribute references in doc
src/HeroParser/SeparatedValues/Reading/Records/CsvRecordOptions.cs        # Doc references
src/HeroParser/SeparatedValues/Reading/Records/InlineCsvMapWrapper.cs     # CsvColumn references
src/HeroParser/SeparatedValues/Reading/Records/MultiSchema/CsvMultiSchemaBinder.cs
src/HeroParser/SeparatedValues/Mapping/CsvMap.cs                          # CsvColumn references
src/HeroParser/SeparatedValues/Mapping/CsvColumnBuilder.cs                # CsvColumn references
src/HeroParser/SeparatedValues/Mapping/ICsvWriteMapSource.cs              # Doc references
src/HeroParser/SeparatedValues/Reading/Rows/CsvRow.cs                     # Doc references
src/HeroParser/SeparatedValues/Reading/Rows/CsvColumn.cs                  # Doc references
src/HeroParser/SeparatedValues/Reading/Rows/ExtensionsToCsvRow.cs         # Doc references
src/HeroParser/SeparatedValues/Reading/Shared/CsvMultiSchemaDispatcherAttribute.cs  # Doc references
src/HeroParser/FixedWidths/Records/Binding/FixedWidthRecordBinder.cs      # FixedWidthColumn refs
src/HeroParser/FixedWidths/Records/FixedWidthReaderBuilder.cs             # FixedWidthColumn refs
src/HeroParser/FixedWidths/Mapping/FixedWidthMap.cs                       # FixedWidthColumn refs
src/HeroParser/FixedWidths/Mapping/FixedWidthColumnBuilder.cs             # FixedWidthColumn refs
src/HeroParser/FixedWidths/Reading/Data/FixedWidthDataReaderColumns.cs    # FixedWidthColumn refs
src/HeroParser/FixedWidths/Writing/FixedWidthRecordWriter.cs              # FixedWidthColumn refs
src/HeroParser/FixedWidths/Writing/FixedWidthWriteOptions.cs              # Doc refs
src/HeroParser/FixedWidths/FixedWidthReadOptions.cs                       # Doc refs
src/HeroParser/FixedWidth.Write.cs                                        # Doc refs
src/HeroParser/FixedWidth.cs                                              # Doc refs
```

### Files to modify (tests — 31 files)

```
tests/HeroParser.Tests/RecordMappingTests.cs
tests/HeroParser.Tests/MultiSchemaTests.cs
tests/HeroParser.Tests/AsyncWriterTests.cs
tests/HeroParser.Tests/CriticalFeaturesTests.cs
tests/HeroParser.Tests/CsvReaderBuilderTests.cs
tests/HeroParser.Tests/LinqExtensionsTests.cs
tests/HeroParser.Tests/PipeReaderTests.cs
tests/HeroParser.Tests/ProductionReadinessTests.cs
tests/HeroParser.Tests/SecurityAndValidationTests.cs
tests/HeroParser.Tests/WriterTests.cs
tests/HeroParser.Tests/ExcludeEmptyColumnsTests.cs
tests/HeroParser.Tests/FixedWidthDataReaderColumnsTests.cs
tests/HeroParser.Tests/Validation/TestRecords.cs
tests/HeroParser.Tests/Validation/CsvColumnValidationTests.cs
tests/HeroParser.Tests/Validation/FixedWidthTestRecords.cs
tests/HeroParser.Tests/Validation/FixedWidthColumnValidationTests.cs
tests/HeroParser.Tests/Mapping/CsvColumnBuilderTests.cs
tests/HeroParser.Tests/Mapping/CsvMapIntegrationTests.cs
tests/HeroParser.Tests/Mapping/FixedWidthColumnBuilderTests.cs
tests/HeroParser.Tests/Mapping/FixedWidthMapTests.cs
tests/HeroParser.Tests/Mapping/FixedWidthMapIntegrationTests.cs
tests/HeroParser.Tests/FixedWidths/FixedWidthBuilderTests.cs
tests/HeroParser.Tests/FixedWidths/FixedWidthSecurityTests.cs
tests/HeroParser.Tests/FixedWidths/FixedWidthFieldLayoutValidationTests.cs
tests/HeroParser.Tests/FixedWidths/FixedWidthAsyncWriterTests.cs
tests/HeroParser.Tests/FixedWidths/FixedWidthPipeReaderTests.cs
tests/HeroParser.Tests/FixedWidths/FixedWidthAsyncStreamReaderTests.cs
tests/HeroParser.Tests/FixedWidths/FixedWidthReaderTests.cs
tests/HeroParser.Generators.Tests/CsvRecordBinderGeneratorTests.cs
tests/HeroParser.Generators.Tests/FixedWidthRecordBinderGeneratorTests.cs
tests/HeroParser.Generators.Tests/GeneratorIntegrationTests.cs
tests/HeroParser.Generators.Tests/Generated/GeneratedPerson.cs
tests/HeroParser.Generators.Tests/Generated/GeneratedAttributed.cs
tests/HeroParser.AotTests/Models/CsvModels.cs
tests/HeroParser.AotTests/Models/FixedWidthModels.cs
```

### Files to modify (benchmarks — 5 files)

```
benchmarks/HeroParser.Benchmarks/PipeReaderBenchmarks.cs
benchmarks/HeroParser.Benchmarks/FixedWidthBenchmarks.cs
benchmarks/HeroParser.Benchmarks/FixedWidthWriterBenchmarks.cs
benchmarks/HeroParser.Benchmarks/BinderOverheadBenchmarks.cs
benchmarks/HeroParser.Benchmarks/MultiSchemaBenchmarks.cs
```

---

## Chunk 1: Unified Attributes — Create & Wire

This chunk creates the 6 new attribute types and `FieldAlignment` in the `HeroParser` root namespace. At the end of this chunk, both old and new attributes exist side-by-side (old ones are not yet deleted).

### Task 1.1: Create attribute files

**Files:**
- Create: `src/HeroParser/Attributes/TabularMapAttribute.cs`
- Create: `src/HeroParser/Attributes/PositionalMapAttribute.cs`
- Create: `src/HeroParser/Attributes/ParseAttribute.cs`
- Create: `src/HeroParser/Attributes/ValidateAttribute.cs`
- Create: `src/HeroParser/Attributes/FormatAttribute.cs`
- Create: `src/HeroParser/Attributes/GenerateBinderAttribute.cs`
- Create: `src/HeroParser/Attributes/FieldAlignment.cs`

- [ ] **Step 1: Create TabularMapAttribute.cs**

```csharp
namespace HeroParser;

/// <summary>
/// Declares how a column in a tabular format (CSV, Excel) maps to a property or field.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class TabularMapAttribute : Attribute
{
    /// <summary>
    /// Column header name. Defaults to the property/field name when omitted.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Zero-based column index. When set, takes precedence over <see cref="Name"/>.
    /// -1 means "use Name or property name".
    /// </summary>
    public int Index { get; init; } = -1;
}
```

- [ ] **Step 2: Create PositionalMapAttribute.cs**

Copy the exact code from the spec (lines 62-105), including the computed `Length`/`End` property logic. Use `namespace HeroParser;`. Reference `FieldAlignment` from the same namespace.

- [ ] **Step 3: Create ParseAttribute.cs**

```csharp
namespace HeroParser;

/// <summary>
/// Specifies the format string for parsing (read-side type conversion).
/// Also serves as the default write format unless <see cref="FormatAttribute"/> overrides it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ParseAttribute : Attribute
{
    /// <summary>
    /// Format string for parsing (e.g., "yyyy-MM-dd" for dates, "N2" for numbers).
    /// </summary>
    public string? Format { get; init; }
}
```

- [ ] **Step 4: Create ValidateAttribute.cs**

Copy the exact code from the spec (lines 128-174). Use `namespace HeroParser;`.

- [ ] **Step 5: Create FormatAttribute.cs**

```csharp
namespace HeroParser;

/// <summary>
/// Specifies write-side serialization options. Overrides <see cref="ParseAttribute.Format"/>
/// for the write direction when <see cref="WriteFormat"/> is set.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class FormatAttribute : Attribute
{
    /// <summary>
    /// Format string for writing. Overrides <see cref="ParseAttribute.Format"/> for write direction.
    /// When omitted, <see cref="ParseAttribute.Format"/> is used for both read and write.
    /// </summary>
    public string? WriteFormat { get; init; }

    /// <summary>
    /// When <c>true</c>, this column is excluded from output if <b>all</b> records have
    /// empty values (<see langword="null"/> or <c>""</c>) for it.
    /// </summary>
    public bool ExcludeIfAllEmpty { get; init; }
}
```

- [ ] **Step 6: Create GenerateBinderAttribute.cs**

```csharp
namespace HeroParser;

/// <summary>
/// Triggers source generation of binder(s) for this record type.
/// The generator inspects properties: <see cref="TabularMapAttribute"/> → tabular binder,
/// <see cref="PositionalMapAttribute"/> → positional binder, both → both.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GenerateBinderAttribute : Attribute { }
```

- [ ] **Step 7: Create FieldAlignment.cs in Attributes folder**

Copy `src/HeroParser/FixedWidths/FieldAlignment.cs` to `src/HeroParser/Attributes/FieldAlignment.cs`. Change namespace from `HeroParser.FixedWidths` to `HeroParser`.

- [ ] **Step 8: Build to verify**

Run: `dotnet build src/HeroParser`

Expected: Build succeeds (both old and new attributes coexist). There may be ambiguity warnings if `FieldAlignment` exists in both namespaces — that's expected and will be resolved when old one is deleted.

- [ ] **Step 9: Commit**

```bash
git add src/HeroParser/Attributes/
git commit -m "feat: Add unified attribute types (TabularMap, PositionalMap, Parse, Validate, Format, GenerateBinder)"
```

---

### Task 1.2: Update source generators to recognize new attributes

**Files:**
- Modify: `src/HeroParser.Generators/CsvRecordBinderGenerator.cs`
- Modify: `src/HeroParser.Generators/FixedWidthRecordBinderGenerator.cs`
- Modify: `src/HeroParser.Generators/CsvMultiSchemaDispatcherGenerator.cs`
- Modify: `src/HeroParser.Generators/GeneratorHelpers.cs`

- [ ] **Step 1: Update CsvRecordBinderGenerator FQN arrays (lines 28-38)**

Replace:
```csharp
private static readonly string[] generateAttributeNames =
[
    "HeroParser.SeparatedValues.Reading.Shared.CsvGenerateBinderAttribute",
    "HeroParser.CsvGenerateBinderAttribute"
];

private static readonly string[] columnAttributeNames =
[
    "HeroParser.SeparatedValues.Reading.Shared.CsvColumnAttribute",
    "HeroParser.CsvColumnAttribute"
];
```

With:
```csharp
private static readonly string[] generateAttributeNames =
[
    "HeroParser.GenerateBinderAttribute"
];

private static readonly string[] columnAttributeNames =
[
    "HeroParser.TabularMapAttribute"
];
```

- [ ] **Step 2: Update CsvRecordBinderGenerator attribute property reading (lines 939-981)**

The switch statement reads named arguments from `[CsvColumn]`. Update it to read from the decomposed attributes. The generator now needs to check three attributes per property: `[TabularMap]`, `[Parse]`, `[Validate]`, and `[Format]`.

Change the property extraction method to scan for multiple attribute types:
- From `[TabularMap]`: read `Name`, `Index`
- From `[Parse]`: read `Format`
- From `[Validate]`: read `NotNull`, `NotEmpty`, `MaxLength`, `MinLength`, `RangeMin`, `RangeMax`, `Pattern`, `PatternTimeoutMs`
- From `[Format]`: read `ExcludeIfAllEmpty`

Add FQN constants for the new attributes:
```csharp
private static readonly string[] parseAttributeNames = ["HeroParser.ParseAttribute"];
private static readonly string[] validateAttributeNames = ["HeroParser.ValidateAttribute"];
private static readonly string[] formatAttributeNames = ["HeroParser.FormatAttribute"];
```

Update the attribute reading loop to check each attribute separately using `GeneratorHelpers.GetFirstMatchingAttribute()`.

- [ ] **Step 3: Update HERO008 diagnostic message**

The diagnostic currently says "CsvColumn requires Name or Index". Update to reference `[TabularMap]`.

- [ ] **Step 4: Update FixedWidthRecordBinderGenerator FQN arrays (lines 28-38)**

Replace:
```csharp
private static readonly string[] generateAttributeNames =
[
    "HeroParser.FixedWidths.Records.Binding.FixedWidthGenerateBinderAttribute",
    "HeroParser.FixedWidthGenerateBinderAttribute"
];

private static readonly string[] columnAttributeNames =
[
    "HeroParser.FixedWidths.Records.Binding.FixedWidthColumnAttribute",
    "HeroParser.FixedWidthColumnAttribute"
];
```

With:
```csharp
private static readonly string[] generateAttributeNames =
[
    "HeroParser.GenerateBinderAttribute"
];

private static readonly string[] columnAttributeNames =
[
    "HeroParser.PositionalMapAttribute"
];
```

- [ ] **Step 5: Update FixedWidthRecordBinderGenerator attribute property reading (lines 1609-1660)**

Same pattern as CSV: decompose the single `[FixedWidthColumn]` switch into scanning `[PositionalMap]`, `[Parse]`, `[Validate]`, `[Format]` attributes.

From `[PositionalMap]`: read `Start`, `Length`, `End`, `PadChar`, `Alignment`
From `[Parse]`: read `Format`
From `[Validate]`: read `NotNull`, `NotEmpty`, `MaxLength`, `MinLength`, `RangeMin`, `RangeMax`, `Pattern`, `PatternTimeoutMs`

- [ ] **Step 6: Update CsvMultiSchemaDispatcherGenerator (lines 32-41)**

Replace:
```csharp
private static readonly string[] generateBinderAttributeNames =
[
    "HeroParser.SeparatedValues.Reading.Shared.CsvGenerateBinderAttribute",
    "HeroParser.CsvGenerateBinderAttribute"
];
```

With:
```csharp
private static readonly string[] generateBinderAttributeNames =
[
    "HeroParser.GenerateBinderAttribute"
];
```

- [ ] **Step 7: Update HERO003 diagnostic message**

Currently says "Record type missing [CsvGenerateBinder]". Update to "[GenerateBinder]".

- [ ] **Step 8: Build generators project**

Run: `dotnet build src/HeroParser.Generators`

Expected: Build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/HeroParser.Generators/
git commit -m "feat: Update source generators to recognize unified attributes"
```

---

### Task 1.3: Update runtime source files

This is the bulk migration — updating 28 source files in `src/HeroParser/` to reference new attribute types and namespaces.

**Strategy:** This is primarily a find-and-replace operation with targeted logic changes for the writer.

- [ ] **Step 1: Update CsvRecordBinderFactory error message**

In `src/HeroParser/SeparatedValues/Reading/Binders/CsvRecordBinderFactory.cs`, update the `GetByteBinder<T>()` exception message (around line 74) to reference `[GenerateBinder]` instead of `[CsvGenerateBinder]`.

- [ ] **Step 2: Update CsvRecordWriter for FormatAttribute**

In `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs`:
- The `WriterTemplate` record (line 125) has `ExcludeFromWriteIfAllEmpty`. This is populated from attribute metadata.
- The runtime reflection path that reads `CsvColumnAttribute.ExcludeFromWriteIfAllEmpty` must be updated to read `FormatAttribute.ExcludeIfAllEmpty`.
- Search for all references to `ExcludeFromWriteIfAllEmpty` in the writer and its factory, and update the attribute lookup.

- [ ] **Step 3: Update CsvMultiSchemaDispatcherAttribute doc comments**

In `src/HeroParser/SeparatedValues/Reading/Shared/CsvMultiSchemaDispatcherAttribute.cs`, update XML doc references from `[CsvGenerateBinder]` to `[GenerateBinder]`.

- [ ] **Step 4: Update all using directives and attribute references across 28 src files**

For each file in the source modification list:
- Replace `using HeroParser.SeparatedValues.Reading.Shared;` with `using HeroParser;` (if needed for attribute access — but since attributes are in the root namespace, imports may not be needed)
- Replace `[CsvColumn(...)]` references in doc comments with `[TabularMap]`, `[Parse]`, `[Validate]`, `[Format]`
- Replace `[CsvGenerateBinder]` references in doc comments with `[GenerateBinder]`
- Replace `[FixedWidthColumn(...)]` references in doc comments with `[PositionalMap]`, `[Parse]`, `[Validate]`
- Replace `[FixedWidthGenerateBinder]` references in doc comments with `[GenerateBinder]`
- Replace `CsvColumnAttribute` type references with the appropriate new attribute types
- Replace `FixedWidthColumnAttribute` type references with the appropriate new attribute types
- Update `using HeroParser.FixedWidths;` where `FieldAlignment` was imported — may need `using HeroParser;` instead (or no change since FieldAlignment is now in root namespace)

- [ ] **Step 5: Update FixedWidth source files for FieldAlignment namespace change**

All files that import `FieldAlignment` from `HeroParser.FixedWidths` need updating. Since `FieldAlignment` moves to `HeroParser` namespace, most files already have this via implicit usings or existing `using HeroParser;`.

Key files:
- `src/HeroParser/FixedWidths/FixedWidthReadOptions.cs` — references FieldAlignment
- `src/HeroParser/FixedWidths/Records/Binding/FixedWidthRecordBinder.cs`
- `src/HeroParser/FixedWidths/Writing/FixedWidthRecordWriter.cs`
- `src/HeroParser/FixedWidths/Mapping/FixedWidthColumnBuilder.cs`

- [ ] **Step 6: Delete old attribute files**

```bash
git rm src/HeroParser/SeparatedValues/Reading/Shared/CsvColumnAttribute.cs
git rm src/HeroParser/SeparatedValues/Reading/Shared/CsvGenerateBinderAttribute.cs
git rm src/HeroParser/FixedWidths/Records/Binding/FixedWidthColumnAttribute.cs
git rm src/HeroParser/FixedWidths/Records/Binding/FixedWidthGenerateBinderAttribute.cs
git rm src/HeroParser/FixedWidths/FieldAlignment.cs
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build src/HeroParser`

Expected: Build succeeds with zero errors.

- [ ] **Step 8: Commit**

```bash
git add -A src/HeroParser/
git commit -m "feat: Migrate runtime source to unified attributes, remove old attribute types"
```

---

### Task 1.4: Update tests, benchmarks, and AOT tests

**Strategy:** Mechanical attribute replacement across all test/benchmark files.

- [ ] **Step 1: Update test record attribute declarations**

Across all 31 test files + 2 generator test files + 2 AOT model files:

Replace patterns:
- `[CsvGenerateBinder]` → `[GenerateBinder]`
- `[FixedWidthGenerateBinder]` → `[GenerateBinder]`
- `[CsvColumn(Name = "X")]` → `[TabularMap(Name = "X")]`
- `[CsvColumn(Index = N)]` → `[TabularMap(Index = N)]`
- `[CsvColumn(Format = "X")]` → `[Parse(Format = "X")]`
- `[CsvColumn(NotNull = true)]` → `[Validate(NotNull = true)]`
- `[CsvColumn(NotEmpty = true)]` → `[Validate(NotEmpty = true)]`
- `[CsvColumn(MaxLength = N)]` → `[Validate(MaxLength = N)]`
- `[CsvColumn(MinLength = N)]` → `[Validate(MinLength = N)]`
- `[CsvColumn(RangeMin = X)]` → `[Validate(RangeMin = X)]`
- `[CsvColumn(RangeMax = X)]` → `[Validate(RangeMax = X)]`
- `[CsvColumn(Pattern = "X")]` → `[Validate(Pattern = "X")]`
- `[CsvColumn(ExcludeFromWriteIfAllEmpty = true)]` → `[Format(ExcludeIfAllEmpty = true)]`
- `[FixedWidthColumn(Start = X, Length = Y)]` → `[PositionalMap(Start = X, Length = Y)]`
- `[FixedWidthColumn(Start = X, End = Y)]` → `[PositionalMap(Start = X, End = Y)]`
- `[FixedWidthColumn(..., PadChar = 'X')]` → include `PadChar` in `[PositionalMap]`
- `[FixedWidthColumn(..., Alignment = FieldAlignment.X)]` → include `Alignment` in `[PositionalMap]`
- `[FixedWidthColumn(..., Format = "X")]` → add `[Parse(Format = "X")]`
- `[FixedWidthColumn(..., NotNull = true)]` → add `[Validate(NotNull = true)]`

**Important:** When a `[CsvColumn]` or `[FixedWidthColumn]` has multiple concerns (e.g., Name + Format + NotNull), it must be decomposed into multiple attributes:

Before:
```csharp
[CsvColumn(Name = "Date", Format = "yyyy-MM-dd", NotNull = true)]
public DateTime Date { get; set; }
```

After:
```csharp
[TabularMap(Name = "Date")]
[Parse(Format = "yyyy-MM-dd")]
[Validate(NotNull = true)]
public DateTime Date { get; set; }
```

- [ ] **Step 2: Update source generator test strings**

In `tests/HeroParser.Generators.Tests/CsvRecordBinderGeneratorTests.cs` and `FixedWidthRecordBinderGeneratorTests.cs`: the test source code strings that are compiled by the generator must use the new attribute names and FQNs.

- [ ] **Step 3: Update using directives in test files**

Remove:
- `using HeroParser.SeparatedValues.Reading.Shared;`
- `using HeroParser.FixedWidths.Records.Binding;`

These are no longer needed since all attributes are in `namespace HeroParser;` which is typically available via implicit usings.

- [ ] **Step 4: Update 5 benchmark files**

Same attribute replacement as tests.

- [ ] **Step 5: Update 2 AOT model files**

Same attribute replacement. In `tests/HeroParser.AotTests/Models/CsvModels.cs` and `FixedWidthModels.cs`.

- [ ] **Step 6: Run all tests**

Run: `dotnet test`

Expected: All tests pass. This verifies the entire attribute migration is correct.

- [ ] **Step 7: Run format check**

Run: `dotnet format --verify-no-changes`

Expected: No formatting issues.

- [ ] **Step 8: Run AOT tests**

Run: `dotnet run --project tests/HeroParser.AotTests -c Release`

Expected: All AOT tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A tests/ benchmarks/
git commit -m "feat: Migrate all tests and benchmarks to unified attributes"
```

---

### Task 1.5: Verification and attribute migration smoke test

- [ ] **Step 1: Write an end-to-end attribute migration test**

Create: `tests/HeroParser.Tests/Attributes/AttributeMigrationTests.cs`

```csharp
[GenerateBinder]
public class MigrationTestRecord
{
    [TabularMap(Name = "Name")]
    [Validate(NotEmpty = true, MaxLength = 50)]
    public string Name { get; set; } = "";

    [TabularMap(Name = "Value")]
    [Parse(Format = "N2")]
    [Validate(RangeMin = 0, RangeMax = 1000)]
    public decimal Value { get; set; }

    [TabularMap(Name = "Date")]
    [Parse(Format = "yyyy-MM-dd")]
    [Validate(NotNull = true)]
    public DateTime Date { get; set; }
}

public class AttributeMigrationTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GeneratedBinder_WithUnifiedAttributes_ParsesCsv()
    {
        var csv = "Name,Value,Date\nWidget,42.50,2026-01-15";
        var records = Csv.Read<MigrationTestRecord>().FromText(csv);

        Assert.Single(records);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal(42.50m, records[0].Value);
        Assert.Equal(new DateTime(2026, 1, 15), records[0].Date);
    }
}
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test --filter "AttributeMigrationTests"`

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add tests/HeroParser.Tests/Attributes/
git commit -m "test: Add attribute migration smoke test"
```

---

## Chunk 2: Excel .xlsx Core Parsing

This chunk builds the internal .xlsx parsing types — the engine that reads ZIP + XML and yields row data.

### Task 2.1: Excel core types

**Files:**
- Create: `src/HeroParser/Excel/Core/ExcelException.cs`
- Create: `src/HeroParser/Excel/Core/ExcelReadOptions.cs`
- Create: `src/HeroParser/Excel/Core/ExcelProgress.cs`
- Create: `src/HeroParser/Excel/Xlsx/XlsxCellType.cs`

- [ ] **Step 1: Create ExcelException**

```csharp
namespace HeroParser.Excel.Core;

/// <summary>
/// Exception thrown when an error occurs during Excel file processing.
/// </summary>
public sealed class ExcelException : Exception
{
    public ExcelException(string message) : base(message) { }
    public ExcelException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 2: Create ExcelReadOptions**

Per spec line 360-373. Sealed record with all options. `Culture` defaults to null (meaning InvariantCulture at runtime, matching CSV).

- [ ] **Step 3: Create ExcelProgress**

```csharp
namespace HeroParser.Excel.Core;

/// <summary>
/// Reports progress during Excel reading operations.
/// </summary>
public readonly struct ExcelProgress(int rowsRead, string sheetName)
{
    public int RowsRead { get; } = rowsRead;
    public string SheetName { get; } = sheetName;
}
```

- [ ] **Step 4: Create XlsxCellType enum**

```csharp
namespace HeroParser.Excel.Xlsx;

internal enum XlsxCellType
{
    Number,          // default, no t attribute or t="n"
    SharedString,    // t="s"
    InlineString,    // t="inlineStr"
    Boolean,         // t="b"
    Error,           // t="e"
    String           // t="str" (formula string result)
}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/HeroParser`

- [ ] **Step 6: Commit**

```bash
git add src/HeroParser/Excel/
git commit -m "feat: Add Excel core types (ExcelException, ExcelReadOptions, ExcelProgress, XlsxCellType)"
```

---

### Task 2.2: XlsxSharedStrings — shared string table parser

**Files:**
- Create: `src/HeroParser/Excel/Xlsx/XlsxSharedStrings.cs`
- Create: `tests/HeroParser.Tests/Excel/XlsxSharedStringsTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
public class XlsxSharedStringsTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Parse_SimpleStrings_ReturnsCorrectValues()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="3" uniqueCount="3">
              <si><t>Hello</t></si>
              <si><t>World</t></si>
              <si><t>Test</t></si>
            </sst>
            """;

        var strings = XlsxSharedStrings.Parse(CreateStream(xml));

        Assert.Equal(3, strings.Count);
        Assert.Equal("Hello", strings[0]);
        Assert.Equal("World", strings[1]);
        Assert.Equal("Test", strings[2]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Parse_RichTextStrings_ConcatenatesRuns()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="1" uniqueCount="1">
              <si><r><t>Hello </t></r><r><t>World</t></r></si>
            </sst>
            """;

        var strings = XlsxSharedStrings.Parse(CreateStream(xml));

        Assert.Single(strings);
        Assert.Equal("Hello World", strings[0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Parse_EmptyTable_ReturnsEmptyList()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>
            """;

        var strings = XlsxSharedStrings.Parse(CreateStream(xml));

        Assert.Empty(strings);
    }

    private static Stream CreateStream(string xml)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HeroParser.Tests --filter "XlsxSharedStringsTests"`

Expected: Compilation error (class doesn't exist yet).

- [ ] **Step 3: Implement XlsxSharedStrings**

```csharp
using System.Xml;

namespace HeroParser.Excel.Xlsx;

/// <summary>
/// Parses the shared string table (xl/sharedStrings.xml) from an .xlsx file.
/// </summary>
internal sealed class XlsxSharedStrings
{
    private readonly string[] strings;

    private XlsxSharedStrings(string[] strings) => this.strings = strings;

    public int Count => strings.Length;
    public string this[int index] => strings[index];

    public static XlsxSharedStrings Parse(Stream stream)
    {
        // Use XmlReader (forward-only) to parse <sst><si><t> and <si><r><t> elements.
        // Concatenate <r><t> runs for rich text.
        // Return string[] for O(1) lookup.
    }
}
```

Implement the `Parse` method using `XmlReader.Create(stream)` with `XmlReaderSettings { IgnoreWhitespace = true }`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/HeroParser.Tests --filter "XlsxSharedStringsTests"`

Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HeroParser/Excel/Xlsx/XlsxSharedStrings.cs tests/HeroParser.Tests/Excel/XlsxSharedStringsTests.cs
git commit -m "feat: Add XlsxSharedStrings parser for shared string table"
```

---

### Task 2.3: XlsxStylesheet — date format detection

**Files:**
- Create: `src/HeroParser/Excel/Xlsx/XlsxStylesheet.cs`
- Create: `tests/HeroParser.Tests/Excel/XlsxStylesheetTests.cs`

- [ ] **Step 1: Write failing tests**

Test cases:
- Built-in date format IDs (14-22) detected as date
- Custom date format patterns (containing y, m, d, h, s) detected as date
- Number formats (0.00, #,##0) NOT detected as date
- Missing styles.xml returns empty stylesheet
- Cell style index maps to correct format

- [ ] **Step 2: Implement XlsxStylesheet**

Parses `xl/styles.xml` to build a mapping of cell style index → is-date-format.

Key logic:
- `<numFmts>` defines custom number formats. Check if pattern contains date characters (`y`, `m`, `d`, `h`, `s`) but NOT if it's a duration-like pattern.
- Built-in format IDs 14-22 are always date formats.
- `<cellXfs>` maps cell style index → number format ID.
- Method: `bool IsDateFormat(int styleIndex)`

- [ ] **Step 3: Run tests**

Expected: All PASS.

- [ ] **Step 4: Commit**

```bash
git add src/HeroParser/Excel/Xlsx/XlsxStylesheet.cs tests/HeroParser.Tests/Excel/XlsxStylesheetTests.cs
git commit -m "feat: Add XlsxStylesheet parser for date format detection"
```

---

### Task 2.4: XlsxWorkbook — sheet name/index mapping

**Files:**
- Create: `src/HeroParser/Excel/Xlsx/XlsxWorkbook.cs`
- Create: `tests/HeroParser.Tests/Excel/XlsxWorkbookTests.cs`

- [ ] **Step 1: Write failing tests**

Test cases:
- Parse workbook with 3 sheets, verify name-to-rId mapping
- GetSheetByName returns correct entry
- GetSheetByIndex returns correct entry (0-based)
- GetFirstSheet returns first sheet
- GetAllSheets returns all
- Missing sheet name throws ExcelException
- Out of range index throws ExcelException

- [ ] **Step 2: Implement XlsxWorkbook**

Parses `xl/workbook.xml` `<sheets>` element. Also parses `xl/_rels/workbook.xml.rels` to resolve rId → worksheet path (e.g., `worksheets/sheet1.xml`).

```csharp
internal sealed class XlsxWorkbook
{
    public record SheetInfo(string Name, int Index, string Path);

    public IReadOnlyList<SheetInfo> Sheets { get; }

    public SheetInfo GetSheetByName(string name) { ... }
    public SheetInfo GetSheetByIndex(int index) { ... }
    public SheetInfo GetFirstSheet() { ... }

    public static XlsxWorkbook Parse(ZipArchive archive) { ... }
}
```

- [ ] **Step 3: Run tests, verify pass**

- [ ] **Step 4: Commit**

```bash
git add src/HeroParser/Excel/Xlsx/XlsxWorkbook.cs tests/HeroParser.Tests/Excel/XlsxWorkbookTests.cs
git commit -m "feat: Add XlsxWorkbook parser for sheet name/index mapping"
```

---

### Task 2.5: XlsxSheetReader — streaming row reader

**Files:**
- Create: `src/HeroParser/Excel/Xlsx/XlsxSheetReader.cs`
- Create: `tests/HeroParser.Tests/Excel/XlsxSheetReaderTests.cs`

- [ ] **Step 1: Write failing tests**

Test cases:
- Read simple sheet with 3 rows, 3 columns
- Shared string cell type resolved correctly
- Inline string cell type read directly
- Numeric cell values read as strings
- Boolean cells: 0 → "false", 1 → "true"
- Error cells treated as null
- Formula cells read cached value
- Date cells (numeric + date style) converted to DateTime string
- Sparse rows (missing cells) filled with empty strings
- Empty sheet yields no rows
- OLE date with Lotus 1900 bug (serial ≤60 adjusted)
- Time-only values (fractional < 1.0) converted to TimeSpan string

- [ ] **Step 2: Implement XlsxSheetReader**

Streaming reader using `XmlReader` over `xl/worksheets/sheetN.xml`.

```csharp
internal sealed class XlsxSheetReader : IDisposable
{
    private readonly XmlReader reader;
    private readonly XlsxSharedStrings sharedStrings;
    private readonly XlsxStylesheet stylesheet;

    public XlsxSheetReader(Stream sheetStream, XlsxSharedStrings sharedStrings, XlsxStylesheet stylesheet) { ... }

    /// <summary>Reads the next row. Returns null when no more rows.</summary>
    public string[]? ReadNextRow() { ... }

    /// <summary>The 1-based Excel row number of the current row.</summary>
    public int CurrentRowNumber { get; }

    public void Dispose() => reader.Dispose();
}
```

Key implementation details:
- Navigate to `<sheetData>` element
- For each `<row>`, iterate `<c>` (cell) elements
- Parse cell reference (e.g., "B3") to extract column index
- Handle sparse rows: if cell references skip columns, fill with empty strings
- Read `t` attribute to determine `XlsxCellType`
- Read `s` attribute for style index (date detection)
- Convert value based on cell type using shared strings and stylesheet
- OLE date conversion: use `DateTime.FromOADate()` with Lotus 1900 bug adjustment

- [ ] **Step 3: Run tests, verify pass**

- [ ] **Step 4: Commit**

```bash
git add src/HeroParser/Excel/Xlsx/XlsxSheetReader.cs tests/HeroParser.Tests/Excel/XlsxSheetReaderTests.cs
git commit -m "feat: Add XlsxSheetReader for streaming sheet row reading"
```

---

### Task 2.6: XlsxReader — orchestrator

**Files:**
- Create: `src/HeroParser/Excel/Xlsx/XlsxReader.cs`
- Create: `src/HeroParser/Excel/Xlsx/XlsxRowAdapter.cs`

- [ ] **Step 1: Implement XlsxReader**

```csharp
internal sealed class XlsxReader : IDisposable
{
    private readonly ZipArchive archive;
    private readonly XlsxWorkbook workbook;
    private readonly XlsxSharedStrings sharedStrings;
    private readonly XlsxStylesheet stylesheet;

    public XlsxReader(Stream stream) { ... }
    public XlsxReader(string path) { ... }

    public XlsxWorkbook Workbook => workbook;

    public XlsxSheetReader OpenSheet(XlsxWorkbook.SheetInfo sheet) { ... }

    public void Dispose() => archive.Dispose();
}
```

Orchestrates: opens ZIP, parses workbook + shared strings + stylesheet upfront, provides `OpenSheet()` to get a streaming reader for a specific sheet.

- [ ] **Step 2: Implement XlsxRowAdapter**

The bridge that converts `string[]` rows from `XlsxSheetReader` into `CsvRow<char>` for the binder pipeline.

```csharp
internal static class XlsxRowAdapter
{
    /// <summary>
    /// Constructs a CsvRow&lt;char&gt; from an array of cell values.
    /// Uses a synthetic delimiter (\x01) to join cells into a contiguous buffer.
    /// </summary>
    public static CsvRow<char> CreateRow(string[] cells, int rowNumber, char[] buffer, int[] columnEnds) { ... }
}
```

Implementation:
- Concatenate cells with `\x01` delimiter into the provided `char[]` buffer
- Build `columnEnds` array: `[-1, pos_of_first_delim, pos_of_second_delim, ...]`
- Return `new CsvRow<char>(buffer.AsSpan(0, totalLength), columnEnds, cells.Length, rowNumber, rowNumber)`

- [ ] **Step 3: Build**

Run: `dotnet build src/HeroParser`

- [ ] **Step 4: Commit**

```bash
git add src/HeroParser/Excel/Xlsx/XlsxReader.cs src/HeroParser/Excel/Xlsx/XlsxRowAdapter.cs
git commit -m "feat: Add XlsxReader orchestrator and XlsxRowAdapter bridge"
```

---

### Task 2.7: Excel test fixture helper

**Files:**
- Create: `tests/HeroParser.Tests/Fixtures/Excel/ExcelTestHelper.cs`

- [ ] **Step 1: Implement programmatic .xlsx creation**

```csharp
using System.IO.Compression;

internal static class ExcelTestHelper
{
    /// <summary>Creates a minimal .xlsx file in memory with the given sheet data.</summary>
    public static MemoryStream CreateXlsx(string sheetName, string[][] rows)
    {
        // Create ZipArchive with:
        // - [Content_Types].xml
        // - xl/workbook.xml (with sheet reference)
        // - xl/_rels/workbook.xml.rels
        // - xl/sharedStrings.xml (collect unique strings)
        // - xl/styles.xml (minimal)
        // - xl/worksheets/sheet1.xml (row/cell data)
        // - _rels/.rels
    }

    /// <summary>Creates a multi-sheet .xlsx file.</summary>
    public static MemoryStream CreateXlsx(Dictionary<string, string[][]> sheets) { ... }

    /// <summary>Creates .xlsx with date-formatted cells for testing date detection.</summary>
    public static MemoryStream CreateXlsxWithDates(string sheetName, (string header, double oleDate, int styleId)[] rows) { ... }
}
```

This helper generates valid .xlsx files programmatically using `System.IO.Compression.ZipArchive` and string-based XML, avoiding any third-party dependency. All Excel tests use this helper.

- [ ] **Step 2: Commit**

```bash
git add tests/HeroParser.Tests/Fixtures/Excel/
git commit -m "test: Add ExcelTestHelper for programmatic .xlsx fixture creation"
```

---

## Chunk 3: Excel Public API

This chunk builds the public API surface: static entry point, builders, and integration tests.

### Task 3.1: Excel static entry point and record builder

**Files:**
- Create: `src/HeroParser/Excel.cs`
- Create: `src/HeroParser/Excel.Read.cs`
- Create: `src/HeroParser/Excel/Reading/ExcelRecordReaderBuilder.cs`
- Create: `tests/HeroParser.Tests/Excel/ExcelReadTests.cs`

- [ ] **Step 1: Write failing tests for basic record reading**

```csharp
[GenerateBinder]
public class SimpleProduct
{
    [TabularMap(Name = "Name")]
    public string Name { get; set; } = "";

    [TabularMap(Name = "Price")]
    public decimal Price { get; set; }

    [TabularMap(Name = "Quantity")]
    public int Quantity { get; set; }
}

public class ExcelReadTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Read_SimpleRecords_FromStream()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"],
            ["Gadget", "24.95", "50"]
        ]);

        var records = Excel.Read<SimpleProduct>().FromStream(xlsx);

        Assert.Equal(2, records.Count);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal(9.99m, records[0].Price);
        Assert.Equal(100, records[0].Quantity);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Read_WithValidation_CollectsErrors()
    {
        // Test [Validate] attributes work through Excel path
    }
}
```

- [ ] **Step 2: Implement ExcelRecordReaderBuilder\<T\>**

Mirror `CsvRecordReaderBuilder<T>` structure. Key methods:

- Sheet selection: `FromSheet(string name)`, `FromSheet(int index)`
- Options: `WithHeaderRow()`, `WithoutHeader()`, `CaseSensitiveHeaders()`, `AllowMissingColumns()`, `SkipRows(int)`, `WithCulture()`, `WithNullValues()`, `WithProgress()`, `WithMaxRows(int)`, `WithMaxColumns(int)`
- Fluent mapping: `Map<TProperty>(Expression, Action<TabularColumnBuilder>?)`
- Terminal methods: `FromFile(string)`, `FromStream(Stream)`, `ToList()`, `ToArray()`, `First()`, `Where()`, `Select()`

Internal flow:
1. Open `XlsxReader` from file/stream
2. Resolve sheet (by name, index, or first)
3. Open `XlsxSheetReader` for the sheet
4. Skip rows if configured
5. Read header row if configured, create binder via `CsvRecordBinderFactory.GetCharBinder<T>()`
6. For each data row: use `XlsxRowAdapter.CreateRow()` to build `CsvRow<char>`, then call `binder.TryBind()`
7. Collect results into `List<T>`
8. Enforce `MaxRowCount`

- [ ] **Step 3: Create Excel.cs and Excel.Read.cs**

```csharp
// Excel.cs
namespace HeroParser;

/// <summary>
/// Entry point for reading Excel (.xlsx) files.
/// </summary>
public static partial class Excel { }

// Excel.Read.cs
namespace HeroParser;

public static partial class Excel
{
    public static ExcelRecordReaderBuilder<T> Read<T>() where T : new()
        => new();

    public static ExcelRowReaderBuilder Read()
        => new();
}
```

- [ ] **Step 4: Run tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HeroParser/Excel.cs src/HeroParser/Excel.Read.cs src/HeroParser/Excel/Reading/ExcelRecordReaderBuilder.cs tests/HeroParser.Tests/Excel/ExcelReadTests.cs
git commit -m "feat: Add Excel.Read<T>() with ExcelRecordReaderBuilder"
```

---

### Task 3.2: Sheet selection tests

**Files:**
- Create: `tests/HeroParser.Tests/Excel/ExcelSheetSelectionTests.cs`

- [ ] **Step 1: Write tests for all four selection modes**

Test cases:
- Default (no `FromSheet`) reads first sheet
- `FromSheet("SheetName")` reads named sheet
- `FromSheet(1)` reads second sheet (0-based)
- `FromSheet("NonExistent")` throws `ExcelException`
- `FromSheet(99)` throws `ExcelException`

Use `ExcelTestHelper.CreateXlsx(Dictionary<string, string[][]>)` with multiple sheets.

- [ ] **Step 2: Run tests, implement any missing logic, verify pass**

- [ ] **Step 3: Commit**

```bash
git add tests/HeroParser.Tests/Excel/ExcelSheetSelectionTests.cs
git commit -m "test: Add sheet selection tests (name, index, first, errors)"
```

---

### Task 3.3: Row-level reading (ExcelRowReaderBuilder)

**Files:**
- Create: `src/HeroParser/Excel/Reading/ExcelRowReaderBuilder.cs`

- [ ] **Step 1: Implement ExcelRowReaderBuilder**

Similar to `CsvRowReaderBuilder`. Returns rows as string arrays rather than typed records. Terminal methods return an enumerable of `ExcelRow` (or reuse an adapter pattern).

- [ ] **Step 2: Write tests for row-level reading**

- [ ] **Step 3: Commit**

---

### Task 3.4: AllSheets — same-type multi-sheet reading

**Files:**
- Create: `src/HeroParser/Excel/Reading/ExcelAllSheetsBuilder.cs`
- Modify: `src/HeroParser/Excel/Reading/ExcelRecordReaderBuilder.cs` (add `AllSheets()` method)

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void AllSheets_SameType_ReturnsDictionaryBySheetName()
{
    var sheets = new Dictionary<string, string[][]>
    {
        ["Q1"] = [["Name", "Value"], ["A", "1"]],
        ["Q2"] = [["Name", "Value"], ["B", "2"]],
    };
    using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

    var result = Excel.Read<NameValue>().AllSheets().FromStream(xlsx);

    Assert.Equal(2, result.Count);
    Assert.Single(result["Q1"]);
    Assert.Equal("A", result["Q1"][0].Name);
    Assert.Single(result["Q2"]);
    Assert.Equal("B", result["Q2"][0].Name);
}
```

- [ ] **Step 2: Implement ExcelAllSheetsBuilder\<T\>**

Returns `Dictionary<string, List<T>>`. Iterates all sheets from `XlsxWorkbook.Sheets`, reads each with the same binder.

- [ ] **Step 3: Run tests, verify pass**

- [ ] **Step 4: Commit**

```bash
git add src/HeroParser/Excel/Reading/ExcelAllSheetsBuilder.cs
git commit -m "feat: Add AllSheets() same-type multi-sheet reading"
```

---

### Task 3.5: Multi-sheet — different types per sheet

**Files:**
- Create: `src/HeroParser/Excel/Reading/ExcelMultiSheetBuilder.cs`
- Create: `src/HeroParser/Excel/Reading/ExcelMultiSheetResult.cs`
- Create: `tests/HeroParser.Tests/Excel/ExcelMultiSheetTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void MultiSheet_DifferentTypes_ReadsCorrectly()
{
    var sheets = new Dictionary<string, string[][]>
    {
        ["Orders"] = [["Product", "Amount"], ["Widget", "9.99"]],
        ["Customers"] = [["Name", "Email"], ["Alice", "a@b.com"]],
    };
    using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

    var result = Excel.Read()
        .WithSheet<OrderRecord>("Orders")
        .WithSheet<CustomerRecord>("Customers")
        .FromStream(xlsx);

    var orders = result.Get<OrderRecord>();
    Assert.Single(orders);
    Assert.Equal("Widget", orders[0].Product);

    var customers = result.Get<CustomerRecord>();
    Assert.Single(customers);
    Assert.Equal("Alice", customers[0].Name);
}

[Fact]
public void MultiSheet_GetUnregisteredType_ThrowsInvalidOperation()
{
    // ...
    Assert.Throws<InvalidOperationException>(() => result.Get<UnregisteredType>());
}
```

- [ ] **Step 2: Implement ExcelMultiSheetBuilder and ExcelMultiSheetResult**

`ExcelMultiSheetBuilder`:
- `WithSheet<T>(string sheetName)` registers a type → sheet name mapping
- Duplicate type registration throws `ArgumentException`
- `FromFile(string)` / `FromStream(Stream)` reads each registered sheet with its binder

`ExcelMultiSheetResult`:
- Internal storage: `Dictionary<Type, object>` (value is `List<T>`)
- `Get<T>()` retrieves and casts, throws `InvalidOperationException` if unregistered

- [ ] **Step 3: Run tests, verify pass**

- [ ] **Step 4: Commit**

```bash
git add src/HeroParser/Excel/Reading/ExcelMultiSheetBuilder.cs src/HeroParser/Excel/Reading/ExcelMultiSheetResult.cs tests/HeroParser.Tests/Excel/ExcelMultiSheetTests.cs
git commit -m "feat: Add multi-sheet reading with different types per sheet"
```

---

### Task 3.6: ExcelDataReader — DbDataReader implementation

**Files:**
- Create: `src/HeroParser/Excel/Reading/Data/ExcelDataReader.cs`
- Create: `src/HeroParser/Excel.DataReader.cs`
- Create: `tests/HeroParser.Tests/Excel/ExcelDataReaderTests.cs`

- [ ] **Step 1: Write failing tests**

Test cases:
- `Read()` advances through rows
- `FieldCount` returns column count
- `GetName(ordinal)` returns header names
- `GetOrdinal(name)` returns column index
- `GetString(ordinal)` returns cell values
- `GetValue(ordinal)` returns cell values
- `IsDBNull(ordinal)` for empty cells
- `Close()` / `Dispose()` cleanup
- `GetSchemaTable()` returns schema

- [ ] **Step 2: Implement ExcelDataReader**

Mirror `CsvDataReader` pattern but backed by `XlsxReader` + `XlsxSheetReader`. Implements `DbDataReader`.

Key differences from CsvDataReader:
- Reads from `XlsxSheetReader` instead of `CsvAsyncStreamReader`
- All values are strings (same as CSV)
- Supports sheet selection via constructor parameter

- [ ] **Step 3: Create Excel.DataReader.cs factory methods**

```csharp
public static partial class Excel
{
    public static ExcelDataReader CreateDataReader(string path) => new(path);
    public static ExcelDataReader CreateDataReader(Stream stream) => new(stream);
}
```

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git add src/HeroParser/Excel/Reading/Data/ExcelDataReader.cs src/HeroParser/Excel.DataReader.cs tests/HeroParser.Tests/Excel/ExcelDataReaderTests.cs
git commit -m "feat: Add ExcelDataReader (DbDataReader) for database bulk loading"
```

---

### Task 3.7: Edge case tests

**Files:**
- Create: `tests/HeroParser.Tests/Excel/ExcelEdgeCaseTests.cs`

- [ ] **Step 1: Write edge case tests**

Test cases:
- Empty .xlsx file (no data rows) — returns empty list
- Sheet with only header row — returns empty list
- Missing cells in sparse rows — filled with empty strings
- Cells with formulas — reads cached value
- Very long string values (>1000 chars)
- MaxRowCount exceeded — throws ExcelException
- MaxColumnCount exceeded — throws ExcelException
- Corrupted .xlsx (not a valid ZIP) — throws ExcelException
- Sheet with boolean cells — "true"/"false" strings
- Sheet with error cells (#REF!, #N/A) — treated as null
- Unicode content (Chinese, Arabic, Emoji)
- Date cells with OLE Automation dates
- SkipRows option
- HasHeaderRow = false (index-based binding)

- [ ] **Step 2: Implement any missing handling, run tests**

- [ ] **Step 3: Commit**

```bash
git add tests/HeroParser.Tests/Excel/ExcelEdgeCaseTests.cs
git commit -m "test: Add Excel edge case tests"
```

---

## Chunk 4: Final Integration & Verification

### Task 4.1: Full build and test verification

- [ ] **Step 1: Run full build**

Run: `dotnet build`

Expected: All projects build with zero errors.

- [ ] **Step 2: Run format check**

Run: `dotnet format --verify-no-changes`

Expected: No formatting issues.

- [ ] **Step 3: Run all tests**

Run: `dotnet test`

Expected: All tests pass (existing + new).

- [ ] **Step 4: Run AOT tests**

Run: `dotnet run --project tests/HeroParser.AotTests -c Release`

Expected: All AOT tests pass.

- [ ] **Step 5: Verify XML doc coverage**

Run: `dotnet build src/HeroParser -warnaserror`

Expected: No CS1591 warnings on new public API members. All new public types in `src/HeroParser/` have XML docs.

- [ ] **Step 6: Regenerate lock files if needed**

Run: `dotnet restore --force-evaluate`

If lock files changed, commit them.

---

### Task 4.2: Update CLAUDE.md

- [ ] **Step 1: Update CLAUDE.md with Excel information**

Add to the CLAUDE.md:
- Excel section describing `Excel.Read<T>()` API
- Unified attribute system description
- Excel test commands

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: Update CLAUDE.md with Excel reading and unified attribute documentation"
```

---

### Task 4.3: Final commit and summary

- [ ] **Step 1: Run git log to verify all commits are clean**

Run: `git log --oneline -20`

- [ ] **Step 2: Verify no debug files or temp files left**

Run: `git status`

Expected: Clean working tree.
