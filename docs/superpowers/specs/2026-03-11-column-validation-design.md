# Column Validation via Source-Generated Inline Checks

**Date:** 2026-03-11
**Status:** Approved (revised after review)

## Summary

Add field-level validation to both CSV and FixedWidth column attributes, emitted inline by source generators. Lazy iteration is preserved — validation errors are collected during enumeration and accessible after iteration completes. Failed rows are not yielded.

## Design Decisions

1. **Validation properties live on column attributes** — `CsvColumnAttribute` and `FixedWidthColumnAttribute` get identical validation properties. No standalone validation attributes (avoids naming conflicts with `System.ComponentModel.DataAnnotations`).
2. **Inline source-generated validation** — The source generator emits validation checks directly in the generated binder code, after parsing each column. Zero reflection, AOT-compatible, compile-time diagnostics for misuse.
3. **Lazy iteration with error collection** — The existing `CsvRecordReader`/FixedWidth reader lazy iteration is preserved. Validation errors are collected during iteration into a `List<ValidationError>` accessible via a new `Errors` property on the reader. Failed rows are **not yielded** (excluded from iteration). Terminal methods like `.ToList()` still work and callers can inspect `.Errors` after iteration.
4. **Name or Index required** — `CsvColumnAttribute` requires explicit `Name` or `Index` (breaking change — no more implicit property-name fallback). `FixedWidthColumnAttribute` already requires `Start`. This is enforced by the source generator only (HERO008 diagnostic), not by the reflection-based path.
5. **FixedWidth gets source-generated binders** — Same pattern as CSV: `[FixedWidthGenerateBinder]` triggers `IIncrementalGenerator` that emits inline binder with validation.
6. **Multi-schema out of scope** — `CsvGenerateDispatcher` and multi-schema dispatch are unchanged by this feature.
7. **Range values use double on attribute, emitted as target type** — `RangeMin`/`RangeMax` are `double` on the attribute (C# attribute limitation), but the generator emits comparisons in the target type's literal format (e.g., `999999.99m` for decimal, `100L` for long). This avoids runtime precision loss.
8. **Pattern regex has timeout** — `Pattern` validation includes a configurable `PatternTimeoutMs` property (default 1000ms). The generator emits a `static readonly Regex` field with the timeout, not inline `Regex.IsMatch` per row.

## Validation Properties (shared across both attributes)

```csharp
// Added to both CsvColumnAttribute and FixedWidthColumnAttribute:
public bool NotNull { get; init; }               // Value must not be null, empty, or whitespace
public bool NotEmpty { get; init; }              // String must not be empty/whitespace
public int MaxLength { get; init; } = -1;        // Max string length (-1 = unchecked)
public int MinLength { get; init; } = -1;        // Min string length (-1 = unchecked)
public double RangeMin { get; init; } = double.NaN;  // Min numeric value
public double RangeMax { get; init; } = double.NaN;  // Max numeric value
public string? Pattern { get; init; }            // Regex pattern for string validation
public int PatternTimeoutMs { get; init; } = 1000;   // Regex timeout in milliseconds
```

Sentinel values (`-1`, `double.NaN`, `null`) mean "unchecked" — generator skips emitting code for unset properties.

## Shared Validation Types

New namespace: `HeroParser.Validation`

```csharp
public readonly struct ValidationError
{
    public int RowNumber { get; init; }
    public int ColumnIndex { get; init; }
    public string? ColumnName { get; init; }
    public string PropertyName { get; init; }
    public string Rule { get; init; }        // "NotNull", "NotEmpty", "MaxLength", etc.
    public string Message { get; init; }
    public string? RawValue { get; init; }

    // Rich diagnostic output:
    // Row 2, Column 'Amount' (index 1), Property 'Amount': [Range] Value must be between 0 and 100000 (raw: '-1.00')
    public override string ToString() { ... }
}
```

## API Shape (Lazy with Validation)

The existing lazy reader pattern is preserved. Validation errors are collected during iteration:

```csharp
// CSV — lazy iteration with validation
var reader = Csv.DeserializeRecords<Transaction>(data, out var bytes);
var records = reader.ToList();  // iterates lazily, collects errors
var errors = reader.Errors;     // validation errors from iteration

// Fluent builder
var reader = Csv.Read<Transaction>().FromText(csvData, out var bytes);
foreach (var record in reader)
{
    // Only valid records are yielded
}
if (reader.Errors.Count > 0)
{
    // Handle validation errors
}

// FixedWidth — same pattern
var reader = FixedWidth.Read<Employee>().FromText(data);
var records = reader.ToList();
var errors = reader.Errors;
```

Failed rows are excluded from iteration. The `Errors` property is populated during enumeration.

## Generated Validation Code (example)

For:
```csharp
[CsvGenerateBinder]
public class Transaction
{
    [CsvColumn(Name = "Id", NotNull = true, NotEmpty = true)]
    public string TransactionId { get; set; }

    [CsvColumn(Name = "Amount", NotNull = true, RangeMin = 0, RangeMax = 999999.99)]
    public decimal Amount { get; set; }

    [CsvColumn(Name = "Currency", NotNull = true, MinLength = 3, MaxLength = 3)]
    public string Currency { get; set; }

    [CsvColumn(Name = "Reference", Pattern = @"^[A-Z]{2}\d{6}$")]
    public string? Reference { get; set; }
}
```

The generator emits (conceptually):
```csharp
// Static regex field (emitted once per binder class)
private static readonly Regex _pattern_Reference = new(@"^[A-Z]{2}\d{6}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));

// In BindInto method — after parsing "Amount" column:
if (amount_isNull)
{
    errors?.Add(new ValidationError { Rule = "NotNull", ... });
    valid = false;
}
else if (parsed_Amount < 0m || parsed_Amount > 999999.99m)  // decimal literal
{
    errors?.Add(new ValidationError { Rule = "Range", ... });
    valid = false;
}

// After parsing "Reference" column:
if (instance.Reference != null && !_pattern_Reference.IsMatch(instance.Reference))
{
    errors?.Add(new ValidationError { Rule = "Pattern", ... });
    valid = false;
}
```

## ICsvBinder Interface Change

The binder interface gains an optional `errors` parameter:

```csharp
public interface ICsvBinder<TElement, T>
    where TElement : unmanaged, IEquatable<TElement>
    where T : new()
{
    bool NeedsHeaderResolution { get; }
    void BindHeader(CsvRow<TElement> headerRow, int rowNumber);
    bool TryBind(CsvRow<TElement> row, int rowNumber, out T result, List<ValidationError>? errors = null);
    bool BindInto(ref T instance, CsvRow<TElement> row, int rowNumber, List<ValidationError>? errors = null);
}
```

All existing binder implementations (reflection-based, descriptor-based, generated) must be updated to accept the new parameter. Non-generated binders simply ignore it (pass-through `null`).

## Compile-Time Diagnostics

New diagnostic codes emitted by generators for attribute misuse:

| Code | Message | Example |
|------|---------|---------|
| HERO004 | `NotEmpty` only applies to string properties | `[CsvColumn(NotEmpty = true)] public int Age` |
| HERO005 | `MaxLength`/`MinLength` only apply to string properties | `[CsvColumn(MaxLength = 10)] public decimal Amount` |
| HERO006 | `RangeMin`/`RangeMax` only apply to numeric properties | `[CsvColumn(RangeMin = 0)] public string Name` |
| HERO007 | `Pattern` only applies to string properties | `[CsvColumn(Pattern = ".*")] public int Id` |
| HERO008 | `CsvColumn` requires `Name` or `Index` | `[CsvColumn(NotNull = true)] public string Name` |

## API Changes (Breaking)

### CSV

- `ICsvBinder<TElement, T>.TryBind` and `BindInto` gain optional `List<ValidationError>? errors` parameter
- `CsvRecordReader<TElement, T>` gains an `Errors` property (`IReadOnlyList<ValidationError>`)
- `CsvColumnAttribute` requires explicit `Name` or `Index` (source generator enforced via HERO008)
- Existing LINQ-like extensions (`.ToList()`, `.First()`, etc.) are preserved

### FixedWidth

- Equivalent changes to FixedWidth binder interface and readers
- `FixedWidthColumnAttribute` gains validation properties

### Existing FixedWidth Validation Attributes

The existing `FixedWidthValidationAttribute` hierarchy (`FixedWidthRequiredAttribute`, `FixedWidthRangeAttribute`, `FixedWidthRegexAttribute`, `FixedWidthStringLengthAttribute`) is **removed**. Their functionality is replaced by the properties on `FixedWidthColumnAttribute`.

### Out of Scope

- Multi-schema CSV dispatch (`CsvGenerateDispatcher`) — unchanged
- Reflection-based binder path — accepts the new `errors` parameter but does not emit validation (validation is source-generator-only)

## Files to Create/Modify

### New Files
- `src/HeroParser/Validation/ValidationError.cs`

### Modified Files
- `src/HeroParser/SeparatedValues/Reading/Shared/CsvColumnAttribute.cs` — add validation properties + PatternTimeoutMs
- `src/HeroParser/FixedWidths/Records/Binding/FixedWidthColumnAttribute.cs` — add validation properties + PatternTimeoutMs
- `src/HeroParser/SeparatedValues/Reading/Binders/ICsvBinder.cs` — add errors parameter
- `src/HeroParser/SeparatedValues/Reading/Binders/*` — all ICsvBinder implementations updated
- `src/HeroParser.Generators/CsvRecordBinderGenerator.cs` — emit validation code + static Regex + diagnostics
- `src/HeroParser.Generators/FixedWidthRecordBinderGenerator.cs` — emit validation code + diagnostics
- `src/HeroParser/SeparatedValues/Reading/Records/CsvRecordReader*.cs` — add Errors property
- All test files — update for new API shape and HERO008

### Removed Files
- `src/HeroParser/FixedWidths/Validation/FixedWidthValidationAttribute.cs`
- `src/HeroParser/FixedWidths/Validation/FixedWidthRequiredAttribute.cs`
- `src/HeroParser/FixedWidths/Validation/FixedWidthRangeAttribute.cs`
- `src/HeroParser/FixedWidths/Validation/FixedWidthRegexAttribute.cs`
- `src/HeroParser/FixedWidths/Validation/FixedWidthStringLengthAttribute.cs`
