# Migrating from HeroParser 1.x to 2.0

HeroParser 2.0 unifies the attribute model across CSV, Fixed-Width, and Excel. The format-specific attributes from 1.x (`[CsvColumn]`, `[FixedWidthColumn]`, `[CsvGenerateBinder]`, `[FixedWidthGenerateBinder]`, and the four stand-alone Fixed-Width validation attributes) are replaced by six concern-separated attributes that work across all formats.

This guide is a drop-in map: for each 1.x construct, find the 2.0 replacement and the one-line code change.

## TL;DR

```csharp
// ---- 1.x ----
[CsvGenerateBinder]
public class Order
{
    [CsvColumn(Name = "OrderId", NotNull = true)]
    public int Id { get; set; }

    [CsvColumn(Name = "Amount", RangeMin = 0, RangeMax = 100_000)]
    public decimal Amount { get; set; }

    [CsvColumn(Name = "OrderDate", Format = "yyyy-MM-dd")]
    public DateTime OrderDate { get; set; }
}

// ---- 2.0 ----
[GenerateBinder]
public class Order
{
    [TabularMap(Name = "OrderId")]
    [Validate(NotNull = true)]
    public int Id { get; set; }

    [TabularMap(Name = "Amount")]
    [Validate(RangeMin = 0, RangeMax = 100_000)]
    public decimal Amount { get; set; }

    [TabularMap(Name = "OrderDate")]
    [Parse(Format = "yyyy-MM-dd")]
    public DateTime OrderDate { get; set; }
}
```

The shape of the reader/writer builder APIs (`Csv.Read<T>()`, `Csv.Write<T>()`, `FixedWidth.Read<T>()`, `FixedWidth.Write<T>()`) is unchanged. Only the attributes on your record classes need updating.

## Attribute rename map

| 1.x attribute                   | 2.0 replacement                    | Notes                                                                                     |
| ------------------------------- | ---------------------------------- | ----------------------------------------------------------------------------------------- |
| `[CsvGenerateBinder]`           | `[GenerateBinder]`                 | Same behavior. Applies to CSV, Fixed-Width, and Excel.                                    |
| `[FixedWidthGenerateBinder]`    | `[GenerateBinder]`                 | Merged with the CSV variant — one attribute, all formats.                                 |
| `[CsvColumn(Name, Index)]`      | `[TabularMap(Name, Index)]`        | Column mapping for CSV **and** Excel. Index default is `-1` ("use Name or property name"). |
| `[CsvColumn(Format = ...)]`     | `[Parse(Format = ...)]`            | Read-side format string (e.g. date parsing).                                              |
| `[CsvColumn(NotNull, NotEmpty, MinLength, MaxLength, RangeMin, RangeMax, Pattern, PatternTimeoutMs)]` | `[Validate(...)]` | Pull validation off the column attribute and onto its own attribute. |
| `[FixedWidthColumn(Start, Length, End, PadChar, Alignment)]` | `[PositionalMap(Start, Length, End, PadChar, Alignment)]` | Fixed-Width position mapping. |
| `[FixedWidthColumn(Format = ...)]` | `[Parse(Format = ...)]` (read) + `[Format(WriteFormat = ...)]` (write, optional) | Read-side format moves to `[Parse]`. Set `[Format(WriteFormat)]` only if write format differs. |
| `[FixedWidthRequired]`          | `[Validate(NotNull = true)]`       | Stand-alone attribute removed.                                                            |
| `[FixedWidthStringLength(Min, Max)]` | `[Validate(MinLength = ..., MaxLength = ...)]` | Stand-alone attribute removed.                                                  |
| `[FixedWidthRange(Min, Max)]`   | `[Validate(RangeMin = ..., RangeMax = ...)]` | Stand-alone attribute removed.                                                       |
| `[FixedWidthRegex(Pattern, TimeoutMs)]` | `[Validate(Pattern = ..., PatternTimeoutMs = ...)]` | Stand-alone attribute removed.                                                |

New in 2.0 and with no 1.x equivalent:

| 2.0 attribute                            | Purpose                                                                                                                                  |
| ---------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `[Format(WriteFormat = ...)]`            | Write-side format override. When omitted, `[Parse(Format)]` is used for both directions.                                                  |
| `[Format(ExcludeIfAllEmpty = true)]`     | Omit the column from output when every record has a null or empty value. Requires materializing all records — not for unbounded streams. |

## Before / after — CSV

```csharp
// ---- 1.x ----
[CsvGenerateBinder]
public class Trade
{
    [CsvColumn(Name = "Ticker", Index = 0)]
    public string Symbol { get; set; } = "";

    [CsvColumn(Name = "TradePrice", Index = 1, RangeMin = 0)]
    public decimal Price { get; set; }

    [CsvColumn(Name = "Qty", Index = 2, NotNull = true)]
    public int Quantity { get; set; }

    [CsvColumn(Name = "TradedAt", Index = 3, Format = "yyyy-MM-dd")]
    public DateTime TradedAt { get; set; }
}

// ---- 2.0 ----
[GenerateBinder]
public class Trade
{
    [TabularMap(Name = "Ticker", Index = 0)]
    public string Symbol { get; set; } = "";

    [TabularMap(Name = "TradePrice", Index = 1)]
    [Validate(RangeMin = 0)]
    public decimal Price { get; set; }

    [TabularMap(Name = "Qty", Index = 2)]
    [Validate(NotNull = true)]
    public int Quantity { get; set; }

    [TabularMap(Name = "TradedAt", Index = 3)]
    [Parse(Format = "yyyy-MM-dd")]
    public DateTime TradedAt { get; set; }
}
```

Convention-based mapping is unchanged: if you don't apply `[TabularMap]`, the property name is used as the column header — exactly like `[CsvColumn]` did in 1.x.

## Before / after — Fixed-Width

```csharp
// ---- 1.x ----
[FixedWidthGenerateBinder]
public class Employee
{
    [FixedWidthColumn(Start = 0, Length = 10)]
    [FixedWidthRequired]
    public string Id { get; set; } = "";

    [FixedWidthColumn(Start = 10, Length = 30)]
    [FixedWidthStringLength(Min = 1, Max = 30)]
    public string Name { get; set; } = "";

    [FixedWidthColumn(Start = 40, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
    [FixedWidthRange(Min = 0)]
    public decimal Salary { get; set; }

    [FixedWidthColumn(Start = 50, Length = 8, Format = "yyyyMMdd")]
    public DateTime HireDate { get; set; }

    [FixedWidthColumn(Start = 58, Length = 12)]
    [FixedWidthRegex(Pattern = @"^\+?\d{10,12}$")]
    public string Phone { get; set; } = "";
}

// ---- 2.0 ----
[GenerateBinder]
public class Employee
{
    [PositionalMap(Start = 0, Length = 10)]
    [Validate(NotNull = true)]
    public string Id { get; set; } = "";

    [PositionalMap(Start = 10, Length = 30)]
    [Validate(MinLength = 1, MaxLength = 30)]
    public string Name { get; set; } = "";

    [PositionalMap(Start = 40, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
    [Validate(RangeMin = 0)]
    public decimal Salary { get; set; }

    [PositionalMap(Start = 50, Length = 8)]
    [Parse(Format = "yyyyMMdd")]
    public DateTime HireDate { get; set; }

    [PositionalMap(Start = 58, Length = 12)]
    [Validate(Pattern = @"^\+?\d{10,12}$")]
    public string Phone { get; set; } = "";
}
```

## Before / after — one record for CSV **and** Fixed-Width

A 2.0 improvement: a single record class can carry both `[TabularMap]` and `[PositionalMap]`, making it usable with CSV, Excel, and Fixed-Width APIs without duplication.

```csharp
[GenerateBinder]
public class Order
{
    [TabularMap(Name = "OrderId")]
    [PositionalMap(Start = 0, Length = 10)]
    public int Id { get; set; }

    [TabularMap(Name = "Amount")]
    [PositionalMap(Start = 10, Length = 12, Alignment = FieldAlignment.Right, PadChar = '0')]
    [Validate(RangeMin = 0)]
    public decimal Amount { get; set; }

    [TabularMap(Name = "OrderDate")]
    [PositionalMap(Start = 22, Length = 8)]
    [Parse(Format = "yyyyMMdd")]        // read format
    [Format(WriteFormat = "yyyy-MM-dd")] // write format — optional; defaults to Parse.Format
    public DateTime OrderDate { get; set; }
}
```

In 1.x this required two separate classes (one per format) plus hand-written mapping. In 2.0 it's one class with layered attributes.

## Excel

Excel reading and writing use `[TabularMap]` (same as CSV). Excel support is new-in-2.0 at the attribute level — there was no `[ExcelColumn]` in 1.x because Excel reading/writing wasn't part of 1.x. See [excel.md](excel.md) for the full API.

## Behavioral changes

These aren't attribute renames — code that compiles after the attribute migration may still behave differently:

1. **Write-side validation runs by default.** In 1.x, validation attributes were read-only. In 2.0, `[Validate]` rules are enforced in both directions. If your writer path previously produced rows that would have failed read-side validation (e.g. `RangeMin` underflow), those rows will now produce a `ValidationError` on write. Use the existing `OnError(...)` hook on the writer builder to control the action (skip / throw / write anyway).

2. **HERO004–HERO008 diagnostics are stricter.** When `[GenerateBinder]` is applied, every `[TabularMap]` must specify either `Name` or `Index`; omitting both now produces build error **HERO008**. In 1.x some cases silently fell back to the property name.

3. **`ExcludeIfAllEmpty` buffers.** Setting `[Format(ExcludeIfAllEmpty = true)]` on any column forces the writer to materialize all records before emitting — it cannot stream. Don't set this flag on unbounded `IEnumerable<T>` sources.

4. **Fluent `.Map(...)` API.** New in 2.0 and an alternative to attributes. If your schema is only known at runtime, use fluent mapping; attribute-based mapping and `[GenerateBinder]` remain the recommended path for AOT/trimming.

## Removed APIs

These types and members are removed in 2.0 with no backward-compatibility shim:

- `CsvColumnAttribute` — replaced by `[TabularMap]` + `[Validate]` + `[Parse]` + `[Format]`.
- `FixedWidthColumnAttribute` — replaced by `[PositionalMap]` + `[Validate]` + `[Parse]` + `[Format]`.
- `CsvGenerateBinderAttribute` — replaced by `[GenerateBinder]`.
- `FixedWidthGenerateBinderAttribute` — replaced by `[GenerateBinder]`.
- `FixedWidthRequiredAttribute` — replaced by `[Validate(NotNull = true)]`.
- `FixedWidthStringLengthAttribute` — replaced by `[Validate(MinLength, MaxLength)]`.
- `FixedWidthRangeAttribute` — replaced by `[Validate(RangeMin, RangeMax)]`.
- `FixedWidthRegexAttribute` — replaced by `[Validate(Pattern, PatternTimeoutMs)]`.

## Mechanical migration recipe

For a medium-sized project, this sequence works:

1. Add `HeroParser` 2.0.0 as the package version — the build will fail on all 1.x attribute references.
2. Global replacements (literal text, case-sensitive):
   - `[CsvGenerateBinder]` → `[GenerateBinder]`
   - `[FixedWidthGenerateBinder]` → `[GenerateBinder]`
   - `[CsvColumn(` → `[TabularMap(`
   - `[FixedWidthColumn(` → `[PositionalMap(`
3. Hand-edit each attribute to split validation rules into a companion `[Validate(...)]` and any `Format = ...` argument into a companion `[Parse(Format = ...)]`. The simple cases are mechanical; the only judgement calls are when `WriteFormat` should differ from `Format` (uncommon).
4. Remove `[FixedWidthRequired]` / `[FixedWidthStringLength]` / `[FixedWidthRange]` / `[FixedWidthRegex]`, folding their constraints into the companion `[Validate(...)]` attribute.
5. Build. Fix any **HERO008** diagnostics by adding `Name` or `Index` to `[TabularMap]` entries that were previously fallback-by-property-name.
6. Run your tests. Watch specifically for new write-side validation failures (see behavioral change #1).

## Reporting issues

If you hit a migration case not covered here, please open a GitHub issue at <https://github.com/KoalaFacts/HeroParser/issues> with a minimal before/after reproduction and we'll either fix the guide or add a deprecation shim.
