# Changelog

All notable changes to HeroParser are documented in this file. This project follows [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-04-24

### Breaking Changes

- **Unified attribute model.** The format-specific attributes from 1.x are removed and replaced by six concern-separated attributes that work across CSV, Excel, and Fixed-Width. See [docs/migration-v1-to-v2.md](docs/migration-v1-to-v2.md) for the rename map and mechanical migration recipe.
  - Removed: `[CsvColumn]`, `[FixedWidthColumn]`, `[CsvGenerateBinder]`, `[FixedWidthGenerateBinder]`, `[FixedWidthRequired]`, `[FixedWidthStringLength]`, `[FixedWidthRange]`, `[FixedWidthRegex]`.
  - Added: `[GenerateBinder]`, `[TabularMap]`, `[PositionalMap]`, `[Parse]`, `[Format]`, `[Validate]`.
- **Write-side validation is now enforced by default.** In 1.x the `NotNull` / `NotEmpty` / `MinLength` / `MaxLength` / `RangeMin` / `RangeMax` / `Pattern` rules ran only on reads. In 2.0 they run on both directions. Use `OnError(...)` on the writer builder to control the action (skip / throw / write anyway) when a rule fails on write.
- **HERO008 build diagnostic.** When `[GenerateBinder]` is applied, every `[TabularMap]` must specify either `Name` or `Index`. Omitting both is now a hard build error (it was a silent fallback in 1.x).
- **Framework targets raised.** 2.0 targets `net8.0`, `net9.0`, and `net10.0`. Earlier framework targets from 1.x are dropped. The source generator continues to target `netstandard2.0` for IDE compatibility.

### Added

#### Excel (.xlsx) reading and writing — new in 2.0
- **Read**: `Excel.Read<T>()` fluent builder — sheet selection, culture, validation, progress, error handling. Zero extra dependencies — uses only `System.IO.Compression` + `System.Xml`.
- **Read variants**: `FromSheet(name|index)`, `AllSheets()` (returns `Dictionary<string, List<T>>`), multi-sheet different-type reads via `Excel.Read().WithSheet<T>(name)`.
- **Row-level read**: `Excel.Read().FromFile(...)` for untyped row iteration.
- **DataReader**: `Excel.CreateDataReader(stream|path)` — `DbDataReader` for streaming into `SqlBulkCopy` and ADO.NET consumers.
- **Write**: `Excel.Write<T>()` fluent builder — sheet naming, date/number formats, culture, output limits, progress, `OnError`.
- **Multi-sheet write**: `Excel.WriteMultiSheet().WithSheet(name, records)` for heterogeneous workbooks.
- **Async**: `ToFileAsync(...)` offloads ZIP writing to the thread pool.

#### Fixed-Width reading and writing — new in 2.0
- **Read**: `FixedWidth.Read<T>()` and `FixedWidth.Read()` fluent builders.
  - Zero-allocation span readers (`FixedWidthCharSpanReader`, `FixedWidthByteSpanReader`).
  - Position mapping via `[PositionalMap(Start, Length, End, PadChar, Alignment)]` — `End` is an alternative to `Length`.
  - `FieldAlignment` (Left / Right / Center / None) for padding-aware trimming.
  - Line-based and fixed-record-length modes; comment lines, empty lines, and row skipping.
  - `AllowShortRows()`, `MaxRecordCount`, `MaxInputSize` DoS protection.
  - `IProgress<FixedWidthProgress>`, custom type converters (`RegisterConverter<T>`), null values (`WithNullValues(...)`).
- **Write**: `FixedWidth.Write<T>()` and `FixedWidth.Write()` fluent builders.
  - `WriteToText`, `WriteToFile`, `WriteToStream` + async variants with `IAsyncEnumerable<T>` support.
  - `FixedWidthStreamWriter` low-level writer.
  - Configurable padding, alignment, newlines.
- **Async stream writer**: `FixedWidth.CreateAsyncStreamWriter(Stream)` for true non-blocking async I/O.
- **Source generator**: `[GenerateBinder]` emits a reflection-free binder for Fixed-Width records (AOT/trim-clean).
- **PipeReader**: streaming reads from network sockets.

#### CSV additions
- **Fluent Reader builder**: `Csv.Read<T>()` — symmetric with the writer builder. `FromText / FromFile / FromStream` terminals plus async variants.
- **Fluent Writer builder**: `Csv.Write<T>()` — `ToText / ToFile / ToStream` terminals plus async. Configure delimiter, quote style, date/number formats, culture.
- **Non-generic builders**: `Csv.Read()` / `Csv.Write()` for manual row-by-row access without a record type.
- **CSV writing**: RFC 4180-compliant `Csv.WriteToText<T>`, `Csv.WriteToFile<T>`, `Csv.WriteToStream<T>` + async variants. Low-level `CsvStreamWriter`.
- **Async stream writer**: `CsvAsyncStreamWriter` with sync fast paths — async overhead only when I/O actually needs to await. Uses `PoolingAsyncValueTaskMethodBuilder` on .NET 6+.
- **Writer options**: `QuoteStyle` (Always / Never / WhenNeeded), `NullValue`, `DateTimeFormat`, `DateOnlyFormat`, `TimeOnlyFormat`, `NumberFormat`, `MaxRowCount`, `OnSerializeError`.
- **SIMD-accelerated writing**: AVX2 / SSE2 single-pass field analysis for quote detection.
- **LINQ-style extensions**: `ToList / ToArray / First / FirstOrDefault / Single / SingleOrDefault / Where / Select / Skip / Take / Count / Any / All / ToDictionary / GroupBy / ForEach` on CSV record readers.
- **Reader options**: `WithProgress`, `OnError` (Skip / UseDefault / Throw actions), `RequireHeaders`, `ValidateHeaders`, `DetectDuplicateHeaders`, `RegisterConverter<T>`.
- **CSV injection protection**: configurable sanitization modes for user-data exports (`CsvInjectionProtection.EscapeWithTab` and friends).
- **Source line number tracking**: `CsvRow.LineNumber` (1-based logical row), `CsvRow.SourceLineNumber` (1-based physical line, multi-line quote aware), `CsvException.SourceLineNumber`. Error messages now include both: `"Row 5 (Line 12): ..."`.
- **Multi-schema dispatch**: source-generated dispatcher (~2.85x faster than runtime) plus a `WithMultiSchema().WithDiscriminator(...)` runtime variant.
- **Delimiter auto-detection**: `CsvDelimiterDetector.DetectDelimiter(...)` with confidence score over `,`, `;`, `|`, `\t`. UTF-8 and UTF-16 input supported.
- **CSV validation**: `Csv.Validate(...)` pre-flight checks for consistent column counts, required headers, row limits, empty files.
- **Schema inference**: `Csv.InferSchema(...)` detects column types (Integer / Decimal / Boolean / DateTime / Guid / String) and nullability from sample data.
- **Converters**: `CsvToFixedWidthConverter.Convert(...)` and `FixedWidthToCsvConverter.Convert(...)`.

#### Cross-cutting
- **Inline field validation**: `[Validate(NotNull, NotEmpty, MinLength, MaxLength, RangeMin, RangeMax, Pattern, PatternTimeoutMs)]` — works on both read and write paths, in all three formats.
- **`[Format(WriteFormat, ExcludeIfAllEmpty)]`**: write-side format override and empty-column pruning.
- **Fluent mapping API**: inline `.Map(e => e.Name, f => ...)` chains on reader and writer builders, for all three formats — useful when the schema is known only at runtime.
- **Validation diagnostics**: `ValidationError` struct carries row number, column name/index, property name, rule, message, raw value; `ValidationException` formats multi-error messages; `ThrowIfAnyError()` fluent API. Compile-time diagnostics `HERO004`–`HERO008` for invalid attribute usage.
- **DataReader support**: `Csv.CreateDataReader(...)`, `FixedWidth.CreateDataReader(...)`, `Excel.CreateDataReader(...)` — `DbDataReader` for `SqlBulkCopy` and ADO.NET consumers. Header mapping, null value detection, column overrides, case-insensitive headers.
- **PipeReader integration**: `Csv.ReadFromPipeReaderAsync(pipe)` for network streaming without buffering the whole payload.
- **Source generators**: `[GenerateBinder]` triggers `CsvRecordBinderGenerator`, `FixedWidthRecordBinderGenerator`, and (for polymorphic dispatch) `CsvMultiSchemaDispatcherGenerator`. Reflection-free, AOT-clean, trim-clean for read paths.
- **AOT tests**: `HeroParser.AotTests` project validates Native AOT compilation for CSV and Fixed-Width read paths.

### Performance
- **CLMUL-based quote handling**: PCLMULQDQ instruction for branchless prefix XOR enables quote-aware SIMD parsing. HeroParser UTF-8 is now faster than Sep 0.12.1 in all tested scenarios, including quoted data (~21% faster on quoted 10k × 25; ~7% on unquoted; 25–45% faster on wide CSVs).
- **SIMD row scanner**: AVX-512 (64-byte chunks), AVX2 (32-byte), ARM NEON (16-byte), scalar fallback. Runtime auto-detection.
- **Fixed 4 KB allocation**: parser allocation is constant regardless of column count or file size (Sep varies 2–13 KB).
- **Async writing is 16–43% faster than sync** at scale with 25–35% less allocation; sync fast paths avoid async overhead when I/O doesn't need to await.
- **CSV writing** is 2–5x faster than Sep 0.12.1 with 35–85% less allocation.
- **ArrayPool buffer reuse**: `CsvCharToByteBinderAdapter`, stream readers and writers use `ArrayPool<char>` / `ArrayPool<byte>`.
- **Stackalloc for small arrays**: column byte lengths use `stackalloc` when ≤128 columns.
- **XlsxWriter**: zero-allocation cell writing path.
- **Cached options**: hot paths avoid repeated property access.

### Fixed
- **`FieldAlignment` enum mapping in source generator**: corrected `FieldAlignment.Center` (value 2) and `FieldAlignment.None` (value 3) so padding trimming behaves correctly.
- **`ArrayPool<char>` in `CsvStreamReader`**: buffer allocation now uses the pool instead of allocating per read, reducing GC pressure.
- **Unbounded buffer growth**: added 128 MB absolute limit (`ABSOLUTE_MAX_BUFFER_SIZE`) in stream readers as DoS protection.

### Security
- **CSV injection protection**: opt-in sanitization for user-controlled fields exported to spreadsheets (`EscapeWithTab`, `EscapeWithQuote`, and other modes).
- **DoS limits**: `MaxRowCount`, `MaxInputSize`, `MaxRecordCount` on all three formats; absolute buffer caps on stream readers.
- **Dependency policy**: GPL-2.0, GPL-3.0, and AGPL-3.0 licenses denied by CI; high-severity vulnerabilities fail the build.

## [1.0.0] - 2025-11-20

- Added configurable RFC compliance options (newlines-in-quotes opt-in, ability to disable quote parsing for speed).
- Expanded parsing helpers: culture-aware and format overloads for date/time types; enum parsing; numeric helpers across byte/short/int/long/float/decimal; timezone parsing.
- Improved UTF-8 parsing consistency and clarified allocation behavior (UTF-8 culture/format parsing decodes to UTF-16).
- Updated CI permissions for publishing test results; benchmarks now toggle RFC options for comparison.
