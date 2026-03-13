# Exclude Empty Columns on CSV Write

## Summary

Add a `CsvWriteOptions.ExcludeEmptyColumns` option that omits columns from CSV output when **all** records have empty values for that column. "Empty" means `null` or empty string (`""`); whitespace values are **not** considered empty.

This feature applies only to record-based writing paths.

## API Surface

### CsvWriteOptions

```csharp
/// <summary>
/// When true, columns where every record's value is empty (null or "") are excluded from output.
/// Whitespace values are not considered empty. Default is false.
/// </summary>
public bool ExcludeEmptyColumns { get; init; } = false;
```

### CsvWriterBuilder\<T\>

```csharp
/// <summary>
/// Excludes columns from the output where all records have empty values.
/// </summary>
public CsvWriterBuilder<T> ExcludingEmptyColumns()
```

### Not affected

- `CsvWriterBuilder` (non-generic, manual row-by-row) — no record concept.
- `CsvStreamWriter` / `CsvAsyncStreamWriter` — low-level writers are unaware of this option.

## Algorithm

Location: `CsvRecordWriter<T>.WriteRecords` and all `WriteRecordsAsync` overloads.

When `ExcludeEmptyColumns` is `false` (default), behavior is unchanged.

When `ExcludeEmptyColumns` is `true`:

1. **Materialize records** — if the input is not already a `List<T>` or `T[]`, materialize to `List<T>`.
2. **Scan with bitmask** — allocate `bool[columnCount]` (`hasNonEmptyValue`). For each record, extract values via property accessors. Mark `hasNonEmptyValue[i] = true` when a value is non-empty. Track `nonEmptyCount`.
3. **Short-circuit** — if `nonEmptyCount == columnCount` during scan, stop scanning. All columns have data; fall through to normal (unfiltered) write path.
4. **Build filtered index array** — from the bitmask, collect an `int[]` of column indices to include.
5. **Write header** — write only the headers at the filtered indices.
6. **Write records** — for each record, extract values at filtered indices into temporary buffers and write via `WriteRowWithFormats`.

### Empty value definition

A column value is considered **empty** when:

| Value | Empty? |
|---|---|
| `null` | Yes |
| `""` (empty string) | Yes |
| `"  "` (whitespace) | **No** |
| Any non-null, non-string type where `ToString()` returns `""` or `null` | Yes |
| Any non-null, non-string type where `ToString()` returns a non-empty string | No |

Fast path: for `string` values, check `value.Length == 0` directly (avoid `ToString()` overhead).

### IAsyncEnumerable paths

All `WriteRecordsAsync` overloads that accept `IAsyncEnumerable<T>` materialize to `List<T>` first via `await foreach`, then reuse the same scan-and-write logic.

### Edge cases

| Scenario | Behavior |
|---|---|
| All columns are empty across all records | Write nothing (no header, no data rows) |
| Zero records | Write header only (existing behavior, all columns included since there's nothing to exclude) |
| Single record, some empty columns | Those columns are excluded |
| `ExcludeEmptyColumns = true` but no columns are empty | Short-circuit detected during scan; normal write path, no perf penalty |
| Null record in collection | Treated as all-empty for that record (does not mark any column as non-empty) |
| Error handler returns `SkipRow` | Row is skipped but its values still participate in the empty-column scan |
| Error handler returns `WriteNull` | The null value participates in the scan (treated as empty) |

### Performance characteristics

- **Memory**: `List<T>` materialization + `bool[columnCount]` bitmask + `int[]` filtered indices + filtered value/format buffers.
- **CPU**: Single scan pass O(N×C) with early termination. Write pass is identical to existing code but over fewer columns.
- **Best case**: All columns non-empty detected in first few records → minimal overhead.
- **No impact when disabled**: Option check is a single `bool` branch before the existing write loop.

## Files to modify

| File | Change |
|---|---|
| `src/HeroParser/SeparatedValues/Writing/CsvWriteOptions.cs` | Add `ExcludeEmptyColumns` property |
| `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs` | Add scan+filter logic in `WriteRecords` and all `WriteRecordsAsync` overloads |
| `src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs` | Add `ExcludingEmptyColumns()` to `CsvWriterBuilder<T>`, wire to options |
| `tests/HeroParser.Tests/WriterTests.cs` or new test file | Tests for all scenarios above |
