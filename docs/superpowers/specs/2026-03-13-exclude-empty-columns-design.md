# Exclude Empty Columns on CSV Write

## Summary

Add a `CsvWriteOptions.ExcludeEmptyColumns` option that omits columns from CSV output when **all** records have empty values for that column. "Empty" means `null` or empty string (`""`); whitespace values are **not** considered empty.

This feature applies only to record-based writing paths, including both reflection-based and source-generated writers (they share the same `WriteRecords`/`WriteRecordsAsync` methods via `CsvRecordWriter<T>`). Fluent map-based writers (`WithMap()`) are also covered since they go through `CsvRecordWriter<T>.CreateFromTemplates`.

## API Surface

### CsvWriteOptions

```csharp
/// <summary>
/// When true, columns where every record's value is empty (null or "") are excluded from output.
/// Whitespace values are not considered empty. Default is false.
/// </summary>
/// <remarks>
/// <para>Requires materializing all records before writing to determine which columns are empty.
/// Not suitable for unbounded streaming scenarios.</para>
/// <para>The emptiness check inspects pre-serialization values from property accessors,
/// not the formatted output. The <see cref="NullValue"/> setting does not affect which columns
/// are considered empty.</para>
/// </remarks>
public bool ExcludeEmptyColumns { get; init; } = false;
```

### CsvWriterBuilder\<T\>

```csharp
/// <summary>
/// Excludes columns from the output where all records have empty values.
/// </summary>
public CsvWriterBuilder<T> WithoutEmptyColumns()
```

### Not affected

- `CsvWriterBuilder` (non-generic, manual row-by-row) — no record concept.
- `CsvStreamWriter` / `CsvAsyncStreamWriter` — low-level writers are unaware of this option.

## Algorithm

Location: `CsvRecordWriter<T>` — all 4 write methods:
1. `WriteRecords(CsvStreamWriter, IEnumerable<T>, bool)` — sync
2. `WriteRecordsAsync(CsvStreamWriter, IAsyncEnumerable<T>, ...)` — async with sync writer
3. `WriteRecordsAsync(CsvAsyncStreamWriter, IAsyncEnumerable<T>, ...)` — async with async writer
4. `WriteRecordsAsync(CsvAsyncStreamWriter, IEnumerable<T>, ...)` — sync enumerable with async writer

When `ExcludeEmptyColumns` is `false` (default), behavior is unchanged.

When `ExcludeEmptyColumns` is `true`:

1. **Materialize records** — if the input is not already a `List<T>` or `T[]`, materialize to `List<T>`. For `IAsyncEnumerable<T>` overloads, materialize via `await foreach`. Check `MaxRowCount` during materialization to fail fast before buffering excess data.
2. **Scan with bitmask** — allocate `bool[columnCount]` (`hasNonEmptyValue`). For each record, extract values via property accessors. Mark `hasNonEmptyValue[i] = true` when a value is non-empty. Track `nonEmptyCount`.
3. **Short-circuit** — if `nonEmptyCount == columnCount` during scan, stop scanning. All columns have data; fall through to normal (unfiltered) write path.
4. **Build filtered index array** — from the bitmask, collect an `int[]` of column indices to include.
5. **Write header** — write only the headers at the filtered indices.
6. **Write records** — for each record, extract values at filtered indices into temporary buffers and write via `WriteRowWithFormats`.

**Note on double extraction**: Property getters are called once during the scan phase and once during the write phase. This is the accepted tradeoff of Approach A — it avoids buffering N×C boxed values in memory. For computed properties, the cost is two evaluations per record.

### Empty value definition

The scan inspects **pre-serialization values** from property accessors, not the formatted output. The `NullValue` option does not affect which columns are considered empty.

| Value | Empty? |
|---|---|
| `null` | Yes |
| `""` (empty string) | Yes |
| `"  "` (whitespace) | **No** |
| Any non-null, non-string type where `ToString()` returns `""` or `null` | Yes |
| Any non-null, non-string type where `ToString()` returns a non-empty string | No |

Fast path: for `string` values, check `value.Length == 0` directly (avoid `ToString()` overhead).

### Interaction with other options

| Option | Interaction |
|---|---|
| `WriteHeader = false` | Filtered columns still apply to data rows; header is simply not written. If all columns are empty, output is empty (no rows). |
| `NullValue` | Does not affect scan. Scan checks pre-serialization values, not formatted output. |
| `MaxRowCount` | Checked during materialization (fail fast). Throws before writing if exceeded. |
| `MaxColumnCount` | Checked against the **original** (pre-filter) column count, not the filtered count. The type defines the column count; filtering is a presentation concern. |
| `OnSerializeError` | During scan: if a getter throws and handler returns `SkipRow`, the row is excluded from writing but columns that successfully extracted before the failure still participate in the scan. The failed column is treated as empty. If handler returns `WriteNull`, the null is treated as empty for the scan. |
| `WriteProgress` | Reported during the write phase only, not during scan/materialization. |

### Edge cases

| Scenario | Behavior |
|---|---|
| All columns are empty across all records | Write nothing (no header, no data rows). This is deliberate — callers who need to preserve row count should not enable this option for all-empty data. |
| Zero records | Write header only (existing behavior, all columns included since there's nothing to exclude) |
| Single record, some empty columns | Those columns are excluded |
| `ExcludeEmptyColumns = true` but no columns are empty | Short-circuit detected during scan; normal write path, no perf penalty |
| Null record in collection | Treated as all-empty for that record (does not mark any column as non-empty) |
| `WriteHeader = false` + all columns empty | Output is completely empty (no rows written) |

### Performance characteristics

- **Memory**: `List<T>` materialization + `bool[columnCount]` bitmask + `int[]` filtered indices + filtered value/format buffers.
- **CPU**: Single scan pass O(N×C) with early termination. Write pass is identical to existing code but over fewer columns. Property getters called twice per record (scan + write).
- **Best case**: All columns non-empty detected in first few records → minimal overhead.
- **No impact when disabled**: Option check is a single `bool` branch before the existing write loop.

## Files to modify

| File | Change |
|---|---|
| `src/HeroParser/SeparatedValues/Writing/CsvWriteOptions.cs` | Add `ExcludeEmptyColumns` property |
| `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs` | Add scan+filter logic in all 4 write methods |
| `src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs` | Add `WithoutEmptyColumns()` to `CsvWriterBuilder<T>`, wire to options |
| `tests/HeroParser.Tests/ExcludeEmptyColumnsTests.cs` | Dedicated test file for this feature |

## Test plan

Tests should cover the following scenarios:

### Core behavior
- Basic: some columns all-empty, verify they are excluded from header and data
- All columns have data → output unchanged
- All columns empty → empty output
- Single record with mix of empty and non-empty columns
- Multiple records where column emptiness varies across records (column non-empty in any record → included)

### Empty value semantics
- `null` values treated as empty
- `""` string values treated as empty
- `"  "` whitespace values treated as **not** empty
- Non-string types where `ToString()` returns `""` → treated as empty
- Numeric zero (`0`, `0.0`) → treated as **not** empty

### Option integration
- `ExcludeEmptyColumns = false` (default) → no filtering
- Combined with `WriteHeader = false` → data rows filtered, no header
- Combined with `WriteHeader = false` + all columns empty → empty output
- Combined with custom `NullValue` (e.g., `"N/A"`) → scan still uses pre-serialization values
- Combined with `MaxRowCount` → throws during materialization if exceeded

### API paths
- Via `CsvWriteOptions` directly (`Csv.WriteToText<T>`)
- Via `CsvWriterBuilder<T>.WithoutEmptyColumns()`
- Via `Csv.WriteToFile<T>` (file output path)
- Async: `Csv.WriteToStreamAsync<T>` with `IAsyncEnumerable<T>`
- Async: `Csv.WriteToStreamAsync<T>` with `IEnumerable<T>`

### Edge cases
- Zero records → header written with all columns (nothing to exclude)
- Null records in collection → treated as all-empty
- Short-circuit: first record has all columns non-empty → scan exits early, output matches non-filtered

### Documentation
- XML docs on `CsvWriteOptions.ExcludeEmptyColumns`
- XML docs on `CsvWriterBuilder<T>.WithoutEmptyColumns()`
