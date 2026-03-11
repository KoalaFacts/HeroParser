# Fluent Mapping API Design

**Date:** 2026-03-12
**Status:** Approved
**Scope:** CSV and FixedWidth fluent column-to-property mapping for read and write

## Problem

HeroParser uses attribute-based configuration exclusively for column-to-property mapping. This prevents mapping types you don't own, using different mappings for the same type, and configuring mappings at runtime.

## Decision Summary

| Decision | Choice |
|----------|--------|
| Read + Write support | Both (unified map) |
| Validation support | Full parity with attributes |
| API shape | Standalone reusable `CsvMap<T>` + inline `.Map()` convenience |
| Subclassing | Supported (CsvHelper-style `ClassMap<T>` pattern) |
| Approach | Enhanced descriptors — extend existing descriptor infrastructure with validation |

## Public API

### CSV

```csharp
// Standalone reusable map
var tradeMap = new CsvMap<Trade>()
    .Map(t => t.Symbol, col => col.Name("Ticker").Required().MaxLength(10))
    .Map(t => t.Price, col => col.Index(2).Format("F2").Range(0, 99999))
    .Map(t => t.Date, col => col.Name("TradeDate").Format("yyyy-MM-dd"));

// Read with map
var records = Csv.Read<Trade>().WithMap(tradeMap).FromFile("trades.csv");

// Write with same map
Csv.Write<Trade>().WithMap(tradeMap).ToFile(records, "out.csv");

// Inline convenience (read-only, builds CsvMap<T> internally)
var records2 = Csv.Read<Trade>()
    .Map(t => t.Symbol, col => col.Name("Ticker").Required())
    .FromFile("trades.csv");

// Subclass pattern
public class TradeMap : CsvMap<Trade>
{
    public TradeMap()
    {
        Map(t => t.Symbol, col => col.Name("Ticker").Required().MaxLength(10));
        Map(t => t.Price, col => col.Index(2).Range(0, 99999));
    }
}
Csv.Read<Trade>().WithMap(new TradeMap()).FromFile("trades.csv");
```

### FixedWidth

```csharp
var empMap = new FixedWidthMap<Employee>()
    .Map(e => e.Id, col => col.Start(0).Length(5).Required())
    .Map(e => e.Name, col => col.Start(5).Length(30).NotEmpty())
    .Map(e => e.Salary, col => col.Start(35).Length(10).Alignment(FieldAlignment.Right));

FixedWidth.Read<Employee>().WithMap(empMap).FromFile("employees.dat");
FixedWidth.Write<Employee>().WithMap(empMap).ToFile(records, "out.dat");
```

## Column Builder Types

### CsvColumnBuilder

Configures one property's mapping for CSV:

```csharp
public class CsvColumnBuilder
{
    // Mapping
    public CsvColumnBuilder Name(string name);
    public CsvColumnBuilder Index(int index);
    public CsvColumnBuilder Format(string format);

    // Validation
    public CsvColumnBuilder Required();
    public CsvColumnBuilder NotEmpty();
    public CsvColumnBuilder MaxLength(int length);
    public CsvColumnBuilder MinLength(int length);
    public CsvColumnBuilder Range(double min, double max);
    public CsvColumnBuilder Pattern(string regex, int timeoutMs = 1000);
}
```

### FixedWidthColumnBuilder

Configures one property's mapping for FixedWidth:

```csharp
public class FixedWidthColumnBuilder
{
    // Layout
    public FixedWidthColumnBuilder Start(int start);
    public FixedWidthColumnBuilder Length(int length);
    public FixedWidthColumnBuilder End(int end);

    // Formatting
    public FixedWidthColumnBuilder PadChar(char c);
    public FixedWidthColumnBuilder Alignment(FieldAlignment alignment);
    public FixedWidthColumnBuilder Format(string format);

    // Validation
    public FixedWidthColumnBuilder Required();
    public FixedWidthColumnBuilder NotEmpty();
    public FixedWidthColumnBuilder MaxLength(int length);
    public FixedWidthColumnBuilder MinLength(int length);
    public FixedWidthColumnBuilder Range(double min, double max);
    public FixedWidthColumnBuilder Pattern(string regex, int timeoutMs = 1000);
}
```

## Internal Architecture

### Map to Infrastructure Wiring

```
CsvMap<T>                          FixedWidthMap<T>
    |                                    |
    |-- BuildReadDescriptor()            |-- BuildReadDescriptor()
    |   -> CsvRecordDescriptor<T>        |   -> FixedWidthRecordDescriptor<T>
    |      (with validation rules)       |      (with validation rules)
    |                                    |
    +-- BuildWriteAccessors()            +-- BuildWriteDefinitions()
        -> PropertyAccessor[]                -> FieldDefinition[]
        (compiled expression getters)        (compiled expression getters)
```

### Descriptor Enhancement

`CsvPropertyDescriptor<T>` gains an optional validation parameter:

```csharp
public readonly struct CsvPropertyDescriptor<T>(
    string name,
    int columnIndex,
    CsvPropertySetter<T> setter,
    bool isRequired = false,
    CsvPropertyValidation? validation = null)
```

`CsvPropertyValidation` carries runtime validation rules:

```csharp
public sealed class CsvPropertyValidation
{
    public bool NotEmpty { get; init; }
    public int? MaxLength { get; init; }
    public int? MinLength { get; init; }
    public double? RangeMin { get; init; }
    public double? RangeMax { get; init; }
    public Regex? Pattern { get; init; }
}
```

Same pattern for FixedWidth with `FixedWidthPropertyDescriptor<T>` and `FixedWidthPropertyValidation`.

### Descriptor Binder Validation

`CsvDescriptorBinder<T>.BindInto()` runs validation after the setter when `prop.Validation` is non-null. Validation errors are added to the existing `List<ValidationError>? errors` parameter. Same behavior as source-generated binders.

`FixedWidthDescriptorBinder<T>` gets identical treatment.

### Setter Construction

The map builds `CsvPropertySetter<T>` delegates using compiled expression trees:

```csharp
// For Trade.Symbol (string property):
// Generates: (ref T instance, ReadOnlySpan<char> value, CultureInfo culture)
//            => instance.Symbol = new string(value);
```

Type-specific parsing logic matches what the source generators emit but is built at runtime via expression compilation. Supports the same types: string, int, long, decimal, double, float, bool, DateTime, DateOnly, TimeOnly, Guid, enums, and their nullable variants.

### Write Accessor Construction

The map builds property accessors using compiled expression trees:

- CSV: `PropertyAccessor` with `Func<object, object?>` getter, header name, format
- FixedWidth: `FieldDefinition` with `Func<T, object?>` getter, start, length, alignment, pad char, format

These are the same types the existing `CsvRecordWriter<T>` and `FixedWidthRecordWriter<T>` already consume.

## AOT / Trimming Annotations

Fluent maps use expression tree compilation and reflection, making them incompatible with AOT and trimming. All public entry points on `CsvMap<T>` and `FixedWidthMap<T>` carry both annotations:

```csharp
[RequiresUnreferencedCode("Fluent mapping uses reflection. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
[RequiresDynamicCode("Fluent mapping uses expression compilation. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
public class CsvMap<T> { ... }
```

The `WithMap()` and inline `Map()` methods on the builders also carry these attributes so the warning propagates to callers.

This matches the existing pattern — `CsvRecordWriter<T>` already uses `[RequiresUnreferencedCode]` for its reflection-based path. Users targeting AOT should use `[CsvGenerateBinder]` / `[FixedWidthGenerateBinder]` attributes instead of fluent maps.

## Builder Integration

### New Methods on CsvRecordReaderBuilder<T>

```csharp
[RequiresUnreferencedCode("...")]
[RequiresDynamicCode("...")]
public CsvRecordReaderBuilder<T> WithMap(CsvMap<T> map);

[RequiresUnreferencedCode("...")]
[RequiresDynamicCode("...")]
public CsvRecordReaderBuilder<T> Map<TProperty>(
    Expression<Func<T, TProperty>> property,
    Action<CsvColumnBuilder>? configure = null);
```

### New Methods on CsvWriterBuilder<T>

```csharp
[RequiresUnreferencedCode("...")]
[RequiresDynamicCode("...")]
public CsvWriterBuilder<T> WithMap(CsvMap<T> map);
```

### New Methods on FixedWidthReaderBuilder<T>

```csharp
[RequiresUnreferencedCode("...")]
[RequiresDynamicCode("...")]
public FixedWidthReaderBuilder<T> WithMap(FixedWidthMap<T> map);

[RequiresUnreferencedCode("...")]
[RequiresDynamicCode("...")]
public FixedWidthReaderBuilder<T> Map<TProperty>(
    Expression<Func<T, TProperty>> property,
    Action<FixedWidthColumnBuilder>? configure = null);
```

### New Methods on FixedWidthWriterBuilder<T>

```csharp
public FixedWidthWriterBuilder<T> WithMap(FixedWidthMap<T> map);
```

### Binder/Writer Resolution Priority

**Reading:**
1. Explicit `WithMap()` -- always wins
2. Source-generated binder (from `[CsvGenerateBinder]`) -- SIMD path
3. Registered descriptor (from factory) -- descriptor path
4. Reflection fallback

**Writing:**
1. Explicit `WithMap()` -- always wins
2. Source-generated writer (from `[CsvGenerateBinder]`)
3. Reflection-based writer (existing fallback)

## Validation Error Handling

| Path | Behavior |
|------|----------|
| Validation errors | Collected in `errors` list, row excluded |
| Parse failures (CSV) | Row excluded, error in list |
| Parse failures (FixedWidth) | Throws exception (pre-existing behavior, not changed here) |
| Missing required column | `CsvException` at header bind time |

Same `ValidationError` struct used across all paths (source-gen and fluent map).

## Breaking Changes

None. This is purely additive:
- `CsvPropertyDescriptor<T>` gains an optional `validation` parameter (default null)
- `CsvDescriptorBinder<T>` gains validation logic (only runs when validation is non-null)
- New public types added; no existing types modified incompatibly

## Not In Scope

- AOT support for fluent maps (expression tree compilation requires JIT; annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`)
- Fixing FixedWidth parse-error-throws-instead-of-collects gap (pre-existing issue)
- `string?` nullable detection in source generators (pre-existing issue)
- Multi-schema support for fluent maps

## New Files

| File | Purpose |
|------|---------|
| `src/HeroParser/SeparatedValues/Mapping/CsvMap.cs` | Standalone CSV map class |
| `src/HeroParser/SeparatedValues/Mapping/CsvColumnBuilder.cs` | CSV column configuration builder |
| `src/HeroParser/SeparatedValues/Mapping/CsvPropertyValidation.cs` | Validation rules container for CSV |
| `src/HeroParser/FixedWidths/Mapping/FixedWidthMap.cs` | Standalone FixedWidth map class |
| `src/HeroParser/FixedWidths/Mapping/FixedWidthColumnBuilder.cs` | FixedWidth column configuration builder |
| `src/HeroParser/FixedWidths/Mapping/FixedWidthPropertyValidation.cs` | Validation rules container for FixedWidth |
| `tests/HeroParser.Tests/Mapping/CsvMapTests.cs` | CSV fluent map unit tests |
| `tests/HeroParser.Tests/Mapping/CsvMapIntegrationTests.cs` | CSV map read/write integration tests |
| `tests/HeroParser.Tests/Mapping/FixedWidthMapTests.cs` | FixedWidth fluent map unit tests |
| `tests/HeroParser.Tests/Mapping/FixedWidthMapIntegrationTests.cs` | FixedWidth map read/write integration tests |

## Modified Files

| File | Change |
|------|--------|
| `src/HeroParser/SeparatedValues/Reading/Shared/CsvPropertyDescriptor.cs` | Add optional `validation` parameter |
| `src/HeroParser/SeparatedValues/Reading/Binders/CsvDescriptorBinder.cs` | Add validation logic in `BindInto()` |
| `src/HeroParser/SeparatedValues/Reading/Records/CsvRecordReaderBuilder.cs` | Add `WithMap()` and `Map()` methods |
| `src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs` | Add `WithMap()` method |
| `src/HeroParser/FixedWidths/Records/Binding/FixedWidthPropertyDescriptor.cs` | Add optional `validation` parameter |
| `src/HeroParser/FixedWidths/Records/Binding/FixedWidthDescriptorBinder.cs` | Add validation logic in `BindInto()` |
| `src/HeroParser/FixedWidths/Records/FixedWidthReaderBuilder.cs` | Add `WithMap()` and `Map()` methods |
| `src/HeroParser/FixedWidths/Writing/FixedWidthWriterBuilder.cs` | Add `WithMap()` method |
