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
    .Map(t => t.Symbol, col => col.Name("Ticker").NotNull().MaxLength(10))
    .Map(t => t.Price, col => col.Index(2).Format("F2").Range(0, 99999))
    .Map(t => t.Date, col => col.Name("TradeDate").Format("yyyy-MM-dd"));

// Read with map — returns CsvRecordReader<byte, T> (ref struct enumerator)
foreach (var trade in Csv.Read<Trade>().WithMap(tradeMap).FromFile("trades.csv", out var bytes))
{
    Console.WriteLine(trade.Symbol);
}

// Read from text — returns CsvRecordReader<char, T>
foreach (var trade in Csv.Read<Trade>().WithMap(tradeMap).FromText(csvString))
{
    Console.WriteLine(trade.Symbol);
}

// Write with same map
Csv.Write<Trade>().WithMap(tradeMap).ToFile("out.csv", trades);

// Inline convenience (read-only shorthand, builds CsvMap<T> internally)
foreach (var trade in Csv.Read<Trade>()
    .Map(t => t.Symbol, col => col.Name("Ticker").NotNull())
    .FromFile("trades.csv", out var bytes2))
{
    Console.WriteLine(trade.Symbol);
}

// Subclass pattern
public class TradeMap : CsvMap<Trade>
{
    public TradeMap()
    {
        Map(t => t.Symbol, col => col.Name("Ticker").NotNull().MaxLength(10));
        Map(t => t.Price, col => col.Index(2).Range(0, 99999));
    }
}
foreach (var trade in Csv.Read<Trade>().WithMap(new TradeMap()).FromFile("trades.csv", out var bytes3))
{
    Console.WriteLine(trade.Symbol);
}
```

### FixedWidth

```csharp
var empMap = new FixedWidthMap<Employee>()
    .Map(e => e.Id, col => col.Start(0).Length(5).NotNull())
    .Map(e => e.Name, col => col.Start(5).Length(30).NotEmpty())
    .Map(e => e.Salary, col => col.Start(35).Length(10).Alignment(FieldAlignment.Right));

// Read — returns FixedWidthReadResult<T> (implements IEnumerable<T>)
var result = FixedWidth.Read<Employee>().WithMap(empMap).FromFile("employees.dat");
foreach (var emp in result) { /* ... */ }
var errors = result.Errors; // validation errors

// Write with same map
FixedWidth.Write<Employee>().WithMap(empMap).ToFile("out.dat", employees);
```

### Type Constraints

Both `CsvMap<T>` and `FixedWidthMap<T>` require `where T : new()`, matching the existing `CsvRecordDescriptor<T>` and `FixedWidthRecordDescriptor<T>` constraints. Types without parameterless constructors are not supported (same limitation as attributes).

### Thread Safety

Map instances are mutable during construction (via `Map()` calls) but should be treated as immutable after construction. Once built and passed to `WithMap()`, the map's internal state is read-only. Thread safety follows the same pattern as `CsvRecordDescriptor<T>` — safe for concurrent reads, not safe for concurrent writes.

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
    public CsvColumnBuilder NotNull();
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
    public FixedWidthColumnBuilder NotNull();
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
    +-- BuildWriteTemplates()            +-- BuildWriteTemplates()
        -> CsvRecordWriter<T>                -> FixedWidthRecordWriter<T>
           .WriterTemplate[]                    .WriterTemplate[]
        (compiled expression getters)        (compiled expression getters)
```

### Descriptor Enhancement

`CsvPropertyDescriptor<T>` gains an optional validation parameter:

```csharp
public readonly struct CsvPropertyDescriptor<T>(
    string name,
    int columnIndex,
    CsvPropertySetter<T> setter,
    bool isNotNull = false,
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

`FixedWidthPropertyDescriptor<T>` gains the same optional validation parameter:

```csharp
public readonly struct FixedWidthPropertyDescriptor<T>(
    string name,
    int start,
    int length,
    char padChar,
    FieldAlignment alignment,
    FixedWidthPropertySetter<T> setter,
    bool isNotNull = false,
    FixedWidthPropertyValidation? validation = null)
```

`FixedWidthPropertyValidation` is identical in shape to `CsvPropertyValidation`.

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

### Write Template Construction

The map builds `WriterTemplate` instances using compiled expression trees for property getters:

- CSV: `CsvRecordWriter<T>.WriterTemplate` — `MemberName`, `SourceType`, `HeaderName`, `AttributeIndex`, `Format`, `Getter` (`Func<T, object?>`)
- FixedWidth: `FixedWidthRecordWriter<T>.WriterTemplate` — `MemberName`, `SourceType`, `Start`, `Length`, `Alignment`, `PadChar`, `Format`, `Getter` (`Func<T, object?>`)

These are the same public record types that source generators register. The existing `CreateFromTemplates()` static factory methods consume them directly. Both `PropertyAccessor` (CSV) and `FieldDefinition` (FixedWidth) are private — `WriterTemplate` is the public contract for providing write configuration externally.

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
| Parse failures (CSV descriptor binder) | Throws `CsvException` (pre-existing behavior, not changed here) |
| Parse failures (FixedWidth) | Throws exception (pre-existing behavior, not changed here) |
| Missing required column | `CsvException` at header bind time |

Same `ValidationError` struct used across all paths (source-gen and fluent map).

**Note:** The CSV descriptor binder currently throws on parse failures (e.g. non-numeric text in an `int` field) rather than collecting them as errors. This is pre-existing behavior. The new validation checks (NotNull, NotEmpty, MaxLength, etc.) run *after* successful parsing and collect errors into the `errors` list. Parse failures continue to throw. This matches the existing source-generated binder behavior for the descriptor path.

## Breaking Changes

None. This is purely additive:
- `CsvPropertyDescriptor<T>` gains an optional `validation` parameter (default null)
- `CsvDescriptorBinder<T>` gains validation logic (only runs when validation is non-null)
- New public types added; no existing types modified incompatibly

## Design Notes

### FixedWidth End() Semantics

`FixedWidthColumnBuilder.End(int end)` specifies the exclusive end position, matching `FixedWidthColumnAttribute.End`. Callers specify `Start + Length` OR `Start + End`, but not all three. If both `Length` and `End` are set, `End` takes precedence (same as the attribute). Validation of field layout (overlaps, gaps) happens when `BuildReadDescriptor()` is called, via the existing `FixedWidthFieldLayoutValidator.Validate()`.

### Custom Converter Interaction

`RegisterConverter<TValue>()` on the builder composes with `WithMap()`. The map defines which properties map to which columns; converters define how to parse specific types. If both are set, the converter handles type parsing while the map handles column mapping and validation. This is the same composition that already works between attributes and converters.

### Inline Map() Asymmetry

Inline `.Map()` is only available on the reader builders, not writer builders. Writing requires a reusable `CsvMap<T>` or `FixedWidthMap<T>` object because the writer needs the full column configuration (header names, formats) to serialize. This is an intentional asymmetry — for read-only quick usage, inline is convenient; for write, use a standalone map.

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
