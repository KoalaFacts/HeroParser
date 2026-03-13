# Excel Reading & Unified Attribute System — v2 Design Spec

## Overview

HeroParser v2 introduces two interconnected features:

1. **Excel (.xlsx) reading** — a new `Excel.Read<T>()` entry point with full parity to the CSV API
2. **Unified attribute system** — replaces `[CsvColumn]`, `[FixedWidthColumn]`, `[CsvGenerateBinder]`, and `[FixedWidthGenerateBinder]` with a composable set of concern-separated attributes

This is a **major version (v2)** release. Old attributes are removed, not deprecated.

## Decisions Log

| Decision | Choice | Rationale |
|---|---|---|
| Excel format | `.xlsx` only | Covers ~99% of modern use cases; well-documented Open XML format |
| Dependencies | Zero new — `System.IO.Compression` + `System.Xml` | Preserves HeroParser's minimal-dependency identity |
| API surface | New `Excel` static class | Excel has its own concepts (sheets, typed cells) that don't map to CSV options |
| Reading scope | Tabular data only | Aligns with HeroParser's identity as a data parsing library |
| Sheet selection | By name, index, first (default), or all | Covers all common use cases |
| All-sheets return | Same-type + multi-type | Uniform and heterogeneous workbooks are both real use cases |
| Attribute system | Unified by concern (Map/Parse/Validate/Format) | Eliminates duplication, enables cross-format records |
| Source generator | Single `[GenerateBinder]` | Infers from mapping attributes — no redundant declaration |
| Migration | Remove old attributes (v2 breaking change) | Clean break, no backward compatibility baggage |

## Part 1: Unified Attribute System

### Design Principles

- **Separated by concern**, not by format
- **Composable** — combine attributes freely on any property
- **Format-agnostic** where possible — Parse, Validate, Format work identically across CSV, Excel, FixedWidth
- **One record, multiple formats** — a record with both `[TabularMap]` and `[PositionalMap]` can be read from CSV, Excel, and FixedWidth

### Attributes

#### `[TabularMap]` — locating data in column-based formats (CSV, Excel)

```csharp
namespace HeroParser;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class TabularMapAttribute : Attribute
{
    /// <summary>
    /// Column header name. Defaults to the property/field name when omitted.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Zero-based column index. When set, takes precedence over Name.
    /// -1 means "use Name or property name".
    /// </summary>
    public int Index { get; init; } = -1;
}
```

**Convention-based mapping:** On a `[GenerateBinder]`-decorated type, properties without any `[TabularMap]` attribute default to `TabularMap(Name = <property name>)` for tabular binding. This preserves the current CSV behavior where unmarked properties bind by header name. Properties without `[PositionalMap]` are excluded from positional binding (since start/length have no sensible default).

#### `[PositionalMap]` — locating data in fixed-width formats

```csharp
namespace HeroParser;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class PositionalMapAttribute : Attribute
{
    private int lengthValue;
    private int endValue = -1;

    /// <summary>
    /// Zero-based starting position of the field in the record.
    /// </summary>
    public int Start { get; init; }

    /// <summary>
    /// Length of the field in characters. If End is specified and Length is not,
    /// length is calculated as End - Start. Length takes precedence over End.
    /// </summary>
    public int Length
    {
        get => lengthValue > 0 ? lengthValue : (endValue > Start ? endValue - Start : 0);
        init => lengthValue = value;
    }

    /// <summary>
    /// Zero-based ending position (exclusive). Alternative to Length.
    /// If both Length and End are specified, Length takes precedence.
    /// </summary>
    public int End
    {
        get => endValue > 0 ? endValue : Start + lengthValue;
        init => endValue = value;
    }

    /// <summary>
    /// Padding character to trim. '\0' means use default from options.
    /// </summary>
    public char PadChar { get; init; } = '\0';

    /// <summary>
    /// Field alignment, determines how trimming is applied.
    /// </summary>
    public FieldAlignment Alignment { get; init; } = FieldAlignment.Left;
}
```

**Note:** `FieldAlignment` must be moved from `HeroParser.FixedWidths` to the root `HeroParser` namespace as part of this migration, since it is now referenced by a root-namespace attribute.

#### `[Parse]` — read-side type conversion

```csharp
namespace HeroParser;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ParseAttribute : Attribute
{
    /// <summary>
    /// Format string for parsing (e.g., "yyyy-MM-dd" for dates, "N2" for numbers).
    /// Also serves as the default write format unless [Format] overrides it.
    /// </summary>
    public string? Format { get; init; }
}
```

#### `[Validate]` — bidirectional constraints (read + write)

```csharp
namespace HeroParser;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class ValidateAttribute : Attribute
{
    /// <summary>
    /// Value must not be null/empty. Checked on both read and write.
    /// </summary>
    public bool NotNull { get; init; }

    /// <summary>
    /// String value must contain at least one non-whitespace character.
    /// Only valid on string properties. Checked on both read and write.
    /// </summary>
    public bool NotEmpty { get; init; }

    /// <summary>
    /// Maximum allowed string length. -1 to disable.
    /// </summary>
    public int MaxLength { get; init; } = -1;

    /// <summary>
    /// Minimum allowed string length. -1 to disable.
    /// </summary>
    public int MinLength { get; init; } = -1;

    /// <summary>
    /// Minimum allowed numeric value (inclusive). NaN to disable.
    /// </summary>
    public double RangeMin { get; init; } = double.NaN;

    /// <summary>
    /// Maximum allowed numeric value (inclusive). NaN to disable.
    /// </summary>
    public double RangeMax { get; init; } = double.NaN;

    /// <summary>
    /// Regex pattern the string value must match.
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Regex timeout in milliseconds. Default 1000ms.
    /// </summary>
    public int PatternTimeoutMs { get; init; } = 1000;
}
```

#### `[Format]` — write-side serialization

```csharp
namespace HeroParser;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class FormatAttribute : Attribute
{
    /// <summary>
    /// Format string for writing. Overrides [Parse].Format for write direction.
    /// When omitted, [Parse].Format is used for both read and write.
    /// </summary>
    public string? WriteFormat { get; init; }

    /// <summary>
    /// When true, this column is excluded from output if all records have
    /// null or empty string values for it. Replaces CsvColumnAttribute.ExcludeFromWriteIfAllEmpty.
    /// </summary>
    public bool ExcludeIfAllEmpty { get; init; }
}
```

**Note:** The `WriteFormat` property name avoids confusion with `[Validate].Pattern` (which is a regex). Writer code that previously read `CsvColumnAttribute.ExcludeFromWriteIfAllEmpty` must be updated to read `FormatAttribute.ExcludeIfAllEmpty` instead — this includes `CsvRecordWriter` and any source-generated writer code.

#### `[GenerateBinder]` — source generator trigger

```csharp
namespace HeroParser;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GenerateBinderAttribute : Attribute { }
```

The source generator inspects properties on the decorated type:
- Properties with `[TabularMap]` → emit a tabular binder (used by CSV and Excel readers)
- Properties with `[PositionalMap]` → emit a positional binder (used by FixedWidth reader)
- Properties with both → emit both binders

#### Source generator attribute discovery

The generators are `netstandard2.0` projects that discover attributes by fully-qualified string name (not by type reference). The following FQNs are the canonical names for v2:

| Attribute | FQN |
|---|---|
| `[GenerateBinder]` | `HeroParser.GenerateBinderAttribute` |
| `[TabularMap]` | `HeroParser.TabularMapAttribute` |
| `[PositionalMap]` | `HeroParser.PositionalMapAttribute` |
| `[Parse]` | `HeroParser.ParseAttribute` |
| `[Validate]` | `HeroParser.ValidateAttribute` |
| `[Format]` | `HeroParser.FormatAttribute` |

All old FQNs are removed. The generators must no longer search for them:

- `HeroParser.SeparatedValues.Reading.Shared.CsvGenerateBinderAttribute`
- `HeroParser.SeparatedValues.Reading.Shared.CsvColumnAttribute`
- `HeroParser.FixedWidths.Records.Binding.FixedWidthGenerateBinderAttribute`
- `HeroParser.FixedWidths.Records.Binding.FixedWidthColumnAttribute`
- `HeroParser.CsvGenerateBinderAttribute`
- `HeroParser.CsvColumnAttribute`
- `HeroParser.FixedWidthGenerateBinderAttribute`
- `HeroParser.FixedWidthColumnAttribute`

#### Multi-schema dispatcher migration

The existing `CsvGenerateDispatcherAttribute` and `CsvSchemaMappingAttribute` must also be updated:
- The multi-schema dispatcher generator currently searches for `CsvGenerateBinderAttribute` to find candidate types — it must be updated to search for `GenerateBinderAttribute` instead
- The dispatcher and schema mapping attributes themselves can keep their `Csv`-prefixed names for now, as multi-schema dispatch is CSV-specific behavior (Excel multi-sheet is handled differently via `WithSheet<T>()`)
- Update `CsvSchemaMappingAttribute` XML doc comments to reference `[GenerateBinder]` instead of `[CsvGenerateBinder]`

#### Migration checklist for user-facing strings

All user-facing error messages and diagnostic descriptions that reference old attribute names must be updated:
- `CsvRecordBinderFactory.GetByteBinder<T>()` exception message → reference `[GenerateBinder]`
- Generator diagnostics (e.g., `HERO003`) → reference `[GenerateBinder]`, `[TabularMap]`, `[PositionalMap]`
- XML doc comments on public types that mention old attributes

### Data Flow

**Read path:** `Map → Parse → Validate → Property`

1. **Map** — extract raw string value from source using TabularMap (column name/index) or PositionalMap (start/length)
2. **Parse** — convert raw string to target type using format string if provided
3. **Validate** — check constraints (NotNull, NotEmpty, Range, etc.) on the parsed value
4. **Property** — assign to the record property

**Write path:** `Property → Validate → Format → Output`

1. **Property** — read the value from the record
2. **Validate** — check the same constraints (bidirectional enforcement)
3. **Format** — serialize using `[Format].WriteFormat`, falling back to `[Parse].Format`
4. **Output** — write to the target format

### Example

```csharp
[GenerateBinder]
public class Order
{
    [TabularMap(Name = "OrderDate")]
    [PositionalMap(Start = 0, Length = 10)]
    [Parse(Format = "yyyy-MM-dd")]
    [Validate(NotNull = true)]
    public DateTime OrderDate { get; set; }

    [TabularMap(Name = "Amount")]
    [PositionalMap(Start = 10, Length = 12)]
    [Validate(RangeMin = 0)]
    [Format(WriteFormat = "F2")]
    public decimal Amount { get; set; }

    [TabularMap(Name = "Customer")]
    [PositionalMap(Start = 22, Length = 50)]
    [Validate(NotEmpty = true, MaxLength = 100)]
    public string Customer { get; set; }
}

// One record type, three formats:
var fromCsv   = Csv.Read<Order>().FromFile("orders.csv");
var fromExcel = Excel.Read<Order>().FromFile("orders.xlsx");
var fromFixed = FixedWidth.Read<Order>().FromFile("orders.dat");
```

## Part 2: Excel (.xlsx) Reading

### API Surface

#### Static entry point — `Excel` class

Parity with `Csv` static class:

```csharp
public static partial class Excel
{
    // Typed record reading (fluent builder)
    public static ExcelRecordReaderBuilder<T> Read<T>() where T : new();

    // Row-level reading (fluent builder)
    public static ExcelRowReaderBuilder Read();

    // Direct deserialization shortcuts
    public static List<T> DeserializeRecords<T>(string path) where T : new();
    public static List<T> DeserializeRecords<T>(Stream stream) where T : new();

    // IDataReader for database bulk loading
    public static ExcelDataReader CreateDataReader(string path);
    public static ExcelDataReader CreateDataReader(Stream stream);
}
```

#### Sheet selection

```csharp
// First sheet (default)
Excel.Read<Order>().FromFile("data.xlsx");

// By name
Excel.Read<Order>().FromSheet("Orders").FromFile("data.xlsx");

// By index (0-based)
Excel.Read<Order>().FromSheet(2).FromFile("data.xlsx");

// All sheets, same record type
Dictionary<string, List<Order>> all = Excel.Read<Order>()
    .AllSheets()
    .FromFile("data.xlsx");

// All sheets, different record types
var result = Excel.Read()
    .WithSheet<Order>("Orders")
    .WithSheet<Customer>("Customers")
    .FromFile("data.xlsx");

List<Order> orders = result.Get<Order>();
List<Customer> customers = result.Get<Customer>();
```

**Multi-sheet result semantics:**
- `Get<T>()` throws `InvalidOperationException` if `T` was not registered via `WithSheet<T>()`
- Each type can only be registered once — calling `WithSheet<Order>("Sheet1").WithSheet<Order>("Sheet2")` is a compile-time or runtime error
- Internal storage is keyed by `Type` — sheet name is used for lookup during reading, type is used for retrieval

#### ExcelReadOptions

```csharp
public sealed record ExcelReadOptions
{
    public bool HasHeaderRow { get; init; } = true;
    public bool CaseSensitiveHeaders { get; init; } = false;
    public bool AllowMissingColumns { get; init; } = false;
    public int SkipRows { get; init; } = 0;
    public int MaxRowCount { get; init; } = 100_000;
    public int MaxColumnCount { get; init; } = 100;
    public IReadOnlyList<string>? NullValues { get; init; }
    public CultureInfo? Culture { get; init; } // null → InvariantCulture (matches CSV behavior)
    public IProgress<ExcelProgress>? Progress { get; init; }
    public int ProgressIntervalRows { get; init; } = 1000;
}
```

**MaxRowCount behavior:** When `MaxRowCount` is exceeded, an `ExcelException` is thrown (same pattern as `CsvException` for CSV). Set to `int.MaxValue` to disable the limit. The builder exposes `WithMaxRowCount(int)` for override.

#### Builder

`ExcelRecordReaderBuilder<T>` mirrors `CsvRecordReaderBuilder<T>` with the addition of sheet selection methods (`FromSheet(string)`, `FromSheet(int)`, `AllSheets()`). All other builder methods (options, mapping, terminal methods) match the CSV builder API.

### .xlsx Parsing Implementation

#### File format

`.xlsx` is a ZIP archive (Open XML / ECMA-376) containing:

```
[Content_Types].xml
xl/
  workbook.xml          — sheet names and order
  sharedStrings.xml     — deduplicated string table
  styles.xml            — number formats (needed to distinguish dates from numbers)
  worksheets/
    sheet1.xml          — row and cell data
    sheet2.xml          — ...
```

#### Reading pipeline

```
.xlsx (ZIP) → ZipArchive (System.IO.Compression)
    → workbook.xml       → sheet name/index/rId mapping
    → sharedStrings.xml  → string lookup table (loaded upfront)
    → styles.xml         → number format lookup (date detection)
    → sheetN.xml         → XmlReader (forward-only streaming) → row/cell extraction
        → cell values as string
        → feed into existing binder pipeline (ICsvBinder<char, T>)
```

#### Internal types

```
XlsxReader (orchestrator — owns ZipArchive lifetime)
├── XlsxWorkbook       — parses workbook.xml, maps sheet name ↔ index ↔ rId
├── XlsxSharedStrings  — parses sharedStrings.xml, provides O(1) string lookup by index
├── XlsxStylesheet     — parses styles.xml, identifies date number formats
└── XlsxSheetReader    — streams sheetN.xml via XmlReader, yields rows of cell values
```

All internal types are in `namespace HeroParser.Excel.Xlsx` and marked `internal`.

#### Cell type handling

| Excel cell type | XML `t` attribute | Handling |
|---|---|---|
| Shared string | `t="s"` | Lookup index in shared strings table |
| Inline string | `t="inlineStr"` | Read `<is><t>` element directly |
| Number | (none or `t="n"`) | Read `<v>`, check style for date format |
| Boolean | `t="b"` | `0` → `"false"`, `1` → `"true"` |
| Error | `t="e"` | Treat as null |
| Formula | has `<f>` element | Read cached `<v>` value (computed result) |

Date detection: when a numeric cell's style references a date number format (built-in IDs 14-22, or custom patterns containing `y`, `m`, `d`, `h`, `s`), convert the OLE Automation date value to a DateTime string before passing to the binder.

**OLE Automation date handling:**
- Uses the 1900 date system (standard for Windows Excel). The 1904 date system (legacy Mac) is not supported.
- Replicates the Lotus 123 leap year bug: Excel incorrectly treats 1900 as a leap year (serial 60 = Feb 29, 1900). Values ≤60 are adjusted by -1 day for correctness, values >60 use standard OLE Automation conversion.
- Time-only values (fractional dates < 1.0) are converted to `TimeSpan` string representation.

#### Row numbering in validation errors

Excel validation errors report 1-based row numbers matching what users see in the Excel application. The row number accounts for `SkipRows` and the header row. For example, if `SkipRows = 2` and `HasHeaderRow = true`, the first data row is reported as row 4 (2 skipped + 1 header + 1 first data row).

#### Binder integration

Excel cell values are extracted as `string` text and fed through the char-based binder path.

**Byte vs char binder path:** The source generators currently emit `ICsvBinder<byte, T>` (UTF-8 optimized). For char-based input (CSV from `string`/`char[]`, and now Excel), the existing `CsvCharToByteBinderAdapter` converts char input to UTF-8 bytes and delegates to the byte binder. Excel will use this same adapter pattern — no new binder interface or char-specific binder generation is needed.

This means:
- Source-generated byte binders are reused via `CsvCharToByteBinderAdapter` (same as CSV char path)
- Descriptor-based (fluent mapping) binders work without modification
- Custom type converters registered via options work without modification
- Validation runs through the same `PropertyValidationRunner`

No new binder interface or implementation is needed for Excel.

**Excel-to-binder bridge:** The `XlsxSheetReader` yields rows as arrays of `string` cell values. To feed these into the existing `ICsvBinder<byte, T>` via `CsvCharToByteBinderAdapter`, the Excel reader must construct synthetic `CsvRow<char>` objects. The approach: concatenate cell values with a synthetic delimiter (e.g., `\x01`) into a contiguous `char[]` buffer, then build the `columnEnds` array pointing to delimiter positions. This mirrors how `CsvCharToByteBinderAdapter` internally constructs `CsvRow<byte>` from char input. The synthetic delimiter is never exposed to users — it is an internal implementation detail of the Excel-to-binder bridge.

### Dependencies

No new NuGet packages. All required APIs are in the BCL:

- `System.IO.Compression.ZipArchive` — reading ZIP entries
- `System.Xml.XmlReader` — forward-only XML streaming
- `System.Globalization.CultureInfo` — number/date formatting

### Performance Considerations

- **Shared strings loaded upfront** — typically small (a few MB even for large files). Stored as `string[]` for O(1) lookup.
- **Streaming XML** — `XmlReader` is forward-only, no DOM allocation. Processes one row at a time.
- **No SIMD** — bottleneck is ZIP decompression + XML parsing, not delimiter scanning.
- **Memory** — only one sheet's row data in memory at a time. Shared strings table is the main memory cost.

## Part 3: Project Structure

### New files

```
src/HeroParser/
  Excel.cs                                    # Static entry point (partial)
  Excel.Read.cs                               # Read builder factory methods
  Excel.DataReader.cs                         # IDataReader factory methods

  Attributes/                                 # Unified attribute home
    TabularMapAttribute.cs
    PositionalMapAttribute.cs
    ParseAttribute.cs
    ValidateAttribute.cs
    FormatAttribute.cs
    GenerateBinderAttribute.cs

  Excel/
    Core/
      ExcelReadOptions.cs
      ExcelException.cs
      ExcelProgress.cs
    Reading/
      ExcelRecordReaderBuilder.cs             # Fluent builder (mirrors CsvRecordReaderBuilder)
      ExcelRowReaderBuilder.cs                # Row-level builder
      ExcelAllSheetsBuilder.cs                # AllSheets() same-type builder
      ExcelMultiSheetBuilder.cs               # WithSheet<T>() multi-type builder
      ExcelMultiSheetResult.cs                # Result container for multi-type
      Data/
        ExcelDataReader.cs                    # DbDataReader implementation
    Xlsx/
      XlsxReader.cs                           # Orchestrator
      XlsxWorkbook.cs                         # workbook.xml parser
      XlsxSharedStrings.cs                    # sharedStrings.xml parser
      XlsxStylesheet.cs                       # styles.xml date detection
      XlsxSheetReader.cs                      # Streaming sheet row reader
      XlsxCellType.cs                         # Cell type enum
```

### Removed files (v2 breaking change)

```
SeparatedValues/Reading/Shared/CsvColumnAttribute.cs
SeparatedValues/Reading/Shared/CsvGenerateBinderAttribute.cs
FixedWidths/Records/Binding/FixedWidthColumnAttribute.cs
FixedWidths/Records/Binding/FixedWidthGenerateBinderAttribute.cs
```

### Moved types (namespace change)

```
FixedWidths/FieldAlignment.cs → root HeroParser namespace (or Attributes/ folder)
```

### Test structure

```
tests/HeroParser.Tests/
  Excel/
    ExcelReadTests.cs                         # Record reading (single sheet)
    ExcelSheetSelectionTests.cs               # By name, index, first, all
    ExcelMultiSheetTests.cs                   # Multi-type sheet reading
    ExcelDataReaderTests.cs                   # IDataReader tests
    ExcelEdgeCaseTests.cs                     # Empty sheets, missing cells, large files
  Attributes/
    TabularMapAttributeTests.cs
    PositionalMapAttributeTests.cs
    ValidateAttributeTests.cs
    ParseAttributeTests.cs
    FormatAttributeTests.cs
    GenerateBinderAttributeTests.cs

tests/HeroParser.Tests/Fixtures/Excel/       # .xlsx test fixtures
```

### Source generator updates

`src/HeroParser.Generators/` updated to:
- Recognize `[GenerateBinder]` instead of `[CsvGenerateBinder]` / `[FixedWidthGenerateBinder]`
- Recognize `[TabularMap]`, `[PositionalMap]`, `[Parse]`, `[Validate]`, `[Format]` instead of `[CsvColumn]` / `[FixedWidthColumn]`
- Emit tabular binder when `[TabularMap]` properties found
- Emit positional binder when `[PositionalMap]` properties found
- Emit both when both present

## Part 4: Scope Summary

### In scope
- Unified attribute system (TabularMap, PositionalMap, Parse, Validate, Format, GenerateBinder)
- Remove old attributes (CsvColumn, FixedWidthColumn, CsvGenerateBinder, FixedWidthGenerateBinder)
- Excel .xlsx reading with full CSV API parity
- Sheet selection (name, index, first, all)
- Multi-sheet reading (same-type and multi-type)
- ExcelDataReader (IDataReader/DbDataReader)
- Source generator updates for new attributes
- Update all existing tests, benchmarks, AOT tests for new attributes
- Test fixtures for .xlsx files

### Out of scope
- `.xls` (legacy binary format)
- `.xlsb` (binary spreadsheet)
- Excel writing
- Cell-level access (non-tabular)
- Formulas (read cached values only)
- Styling, formatting, merged cells
- Charts, images, pivot tables
- Async Excel reading (deferred — `ZipArchive` + `XmlReader` async support can be added in a future release)
- Per-property culture overrides in `[Parse]` (intentionally deferred to avoid attribute bloat; culture is set at the options level via `ExcelReadOptions.Culture` / `CsvRecordOptions.Culture`)
- 1904 date system support (extremely rare in modern files)
