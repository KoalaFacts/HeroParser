# Changelog

All notable changes to HeroParser are documented in this file. This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [2.4.1] - 2026-06-06

Security hardening, streaming robustness, agentic validation, and HTB documentation release. Delivers global Excel XML Zip-bomb/XXE mitigations, ReDoS timeout guards, buffer-boundary split streaming safety, endianness corrections for HTB binary files, memory-optimized zero-allocation JSONL parsers, and dedicated HTB documentation.

### Added
- **Security Hardening & XXE Mitigations**:
  - Centralized ZIP entry-size checks and secure `XmlReaderSettings` (DtdProcessing.Prohibit, XmlResolver disabled) in `XlsxXml.cs` to prevent XML External Entity (XXE) and Billion Laughs XML bombs.
  - Thread-safe compiled `Regex` caching with a default 1000ms matching timeout in `SchemaMetadata.cs` to prevent Regular Expression Denial of Service (ReDoS) during tool call validation.
  - Safe stack allocation sizing guards with `ArrayPool<byte>` fallback checks on all dynamic buffer `stackalloc` occurrences, eliminating stack exhaustion vectors.
- **PipeReader Split-Boundary Protection**:
  - Hardened PipeReader row parsing in `Csv.PipeReader.cs` and `Csv.PipeSequenceReader.cs` to prevent row truncation or corrupted splits when delimiter, escape, double-quote, or CRLF sequences land exactly on segment boundaries.
  - Integrated identical CRLF segment split guards in `FixedWidth.PipeReader.cs`.
- **JSONL Memory Optimizations**:
  - Upgraded `JsonlLineReader.cs` to return zero-copy `ReadOnlyMemory<byte>` slices directly from the rented buffer pool.
  - Eliminated `.ToArray()` heap allocations per record in `JsonlDataReader.cs` by parsing memory slices directly.
  - Added transaction-safe buffered JSONL writing in `JsonlStreamWriter.cs` using a recycled `ArrayBufferWriter<byte>` to prevent output stream corruption on mid-record serialization failures.
- **HTB Endianness & Schema Syncing**:
  - Ensured correct big-endian byte-order reversal for floats and doubles in `HtbStreamReader.cs` and `HtbStreamWriter.cs`.
  - Synchronized ordinal properties sorting in both source generator and reflection-based schema builders to prevent dynamic schema desyncs.
- **Agent parameter binding**:
  - Expanded type mapping support for `Guid`, `DateTime`, `DateTimeOffset`, and `TimeSpan` parameters in `SchemaMetadata.MapFromToolCall`.

### Documentation
- **HTB Format Promotion & References**:
  - Created a comprehensive API reference at `docs/htb.md` covering fluent reader/writer builders, direct CSV ↔ HTB conversions, option specs, and AOT compiler integration guidelines.
  - Integrated HTB quick starts, features, and layout definitions directly into `README.md`.

## [2.4.0] - 2026-05-29

High-performance write-path pre-allocated capacity release. Applies structural backing buffer capacity pre-allocation optimizations across all sync, string-generating text pipelines (CSV, Fixed-Width, and JSONL) when record collection counts are known, yielding substantial throughput improvements and eliminating multiple transient array allocation and copy resizing cycles.

### Added
- **JSONL Pre-allocated Capacity Optimization**: Implemented dynamic record count inspection (`ICollection`/`IReadOnlyCollection<T>`) inside `Jsonl.WriteToText<T>` and `JsonlWriterBuilder<T>.ToText` sync facade methods to pre-allocate backing `MemoryStream` capacities with an estimated average record size (`count * 128`).
- **CSV Pre-allocated Capacity Optimization**: Implemented backing `StringBuilder` pre-allocation (`count * 64`) inside `Csv.WriteToText<T>` and `CsvWriterBuilder<T>.ToText` to bypass default builder resizing overhead.
- **Fixed-Width Pre-allocated Capacity Optimization**: Implemented precise compile-time backing `StringBuilder` pre-allocation (`count * (RecordLength + NewLine.Length)`) inside `FixedWidth.WriteToText<T>` and `FixedWidthWriterBuilder<T>.ToText` for perfect capacity budgeting.

### Performance
- **1.9x Faster JSONL Serialization**: Pre-allocation combined with source-generated `JsonTypeInfo<T>` serialization of 100k records completes in just **17.01 ms** (a **1.9x throughput speedup** compared to standard reflection-based serializing).
- **Up to 64% Speedup on CSV/Fixed-Width String Generation**: Pre-allocating the string builder capacity improves CSV and Fixed-Width facade throughput by **35% to 64%** depending on column count and record volume.
- **Resizing GC Overhead Eliminated**: Completely avoids dynamic backing buffer doubling and allocation copying, ensuring highly stable, deterministic heap memory footprints during large in-memory text serialization.

### Integrity & Safety
- **100% Native AOT & Trim-Safe**: Backing capacity pre-allocation logic is fully trim-safe and compiles cleanly with zero AOT warnings across .NET 8.0, 9.0, and 10.0. All Native AOT compilation validation tests pass perfectly.
- **Zero Public API Breakages**: Keep all public facades and builders completely unchanged, delivering seamless performance upgrades automatically.

## [2.3.0] - 2026-05-27

AI-Native tabular capabilities release. Introduces five high-performance, Native AOT-compliant features linking C# tabular streams to LLM and vector embedding pipelines, along with 100% unit test coverage and high-impact micro-optimizations.

### Added
- **LLM Structured Output Repair Binder (`LlmRepair`)**: Strips markdown formatting block wraps and repairs unbalanced quotes or truncation on final rows for stream cutoff recovery. Streams directly into strongly-typed records.
- **Tabular Vector Embedding Batching Pipeline (`ToLlmEmbeddingsAsync`)**: Semantic chunking batched vector generator pipeline pairing record chunks with vectors with a zero-allocation, token-budgeted streaming pipeline.
- **AI-Driven Tabular Context Profiler (`TabularContextProfiler`)**: Zero-allocation, reflection-safe statistics profiling generating rich markdown card summaries for system prompt injection.
- **Agent Tool Argument Mapper (`SchemaMetadata.MapFromToolCall`)**: Reflection-free, case-insensitive parameter binder mapping tool arguments to records and enforcing all `[Validate]` constraints with detailed error reports.
- **Token-Bounded JSON Chunker (`JsonLlmChunker`)**: Streams records into token-bounded JSON arrays. Includes a Native AOT-safe overload accepting `JsonTypeInfo<T>` to eliminate reflection trim warnings entirely.

### Performance & Optimizations
- **O(1) Dictionary Mapping**: Shifted the key-value matching logic in `MapFromToolCall` to use O(1) case-insensitive dictionary lookups, eliminating nested search loops completely.
- **Explicit NaN Boundaries**: Replaced implicit double comparative checks with explicit `double.IsNaN` checks to protect constraint validation against missing default bounds.
- **100% Test Coverage**: Expanded testing to ensure 100% test coverage across all five features.

## [2.2.1] - 2026-05-26

Performance, zero-allocation, and multi-schema modernization release. Introduces zero-allocation Excel alternate lookups, modernized reflection-free multi-schema generated dispatching with dynamic header resolution, and AVX-512 write-path character scanning. Fully backward compatible and Native AOT compliant.

### Added
- **JSONL source-generated binders — high-performance reflection-free parsing**: Implemented `IJsonlBinder<T>` and `JsonlRecordBinderFactory` registries to support ultra-high performance JSONL deserialization via modern incremental source generation (`JsonlRecordBinderGenerator`). Emits sequential `Utf8JsonReader` parsing blocks that map keys via `SequenceEqual` against static `ReadOnlySpan<byte>` property name UTF-8 spans with case-insensitive, camelCase, lowercase, and custom `TabularMap`/`JsonPropertyName` override support. Prioritizes the generated binder automatically inside both synchronous and asynchronous `Jsonl` reading loops.
- **Span-based Excel Writing API**: Added a new public `WriteCellString(int columnIndex, ReadOnlySpan<char> value)` cell writer API to `SheetWriter` in `XlsxWriter.cs` to write text from character spans with zero heap overhead and automatic Excel injection protection checks.
- **Modernized Multi-Schema Generated Dispatcher**: Enhanced `CsvMultiSchemaDispatcherGenerator.cs` to dynamically resolve discriminator column indices from the header row via allocation-free byte-span comparison (`SpanEqualsBYTE`). Emits header-binding propagation (`BindHeaderBytes`) to all underlying record binders, eliminating all potential reflection or dynamic resolution overheads.

### Performance
- **Zero-Allocation Excel alternate lookup**: Added a `GetOrAdd(ReadOnlySpan<char> value)` overload to `XlsxSharedStringTable.cs` leveraging .NET 9+'s `.GetAlternateLookup<ReadOnlySpan<char>>()` to map spans directly to existing shared string indices with **zero managed string allocations** during cell writing.
- **AVX-512 Write-Path SIMD Quoting**: Implemented `AnalyzeFieldSimd512` in `CsvWriterQuoting.cs` using a 512-bit vector (`Vector512<ushort>`) to scan 32 characters (64 bytes) in a single CPU instruction, matching the performance of the read-path SIMD scanner.
- **2.3x Faster JSONL Reading & 70%+ Heap Allocation Reductions**: Integrating the generated `IJsonlBinder<T>` into the core `Jsonl` reading loops yields a massive **2.3x throughput speedup** and up to **72% fewer managed heap allocations** (saving 12 MB of allocations on 100,000 rows) compared to `System.Text.Json`'s baseline reflection pipeline. Fully trim-safe and Native AOT compatible.
- **100% Allocation-Free automatic delimiter detection**: Refactored `CsvDelimiterDetector.cs` to utilize stack-allocated spans (`stackalloc int[]`) for default sample rows (<=128) and rented buffers for larger samples, replacing the heap-allocated dictionaries and lists. The main `DetectDelimiter` APIs are now completely allocation-free (0 bytes allocated on both UTF-16 spans and UTF-8 byte spans).
- **Zero-Allocation Excel worksheet XML parser**: Completely refactored `XlsxSheetReader.cs` to iterate rows and cells sequentially using node depth checks (`reader.Depth`), entirely eliminating the millions of garbage-collected `XmlReader.ReadSubtree()` wrapper allocations created per spreadsheet read.
- **Zero-Allocation Excel shared string parser**: Bypassed all remaining `XmlReader.ReadSubtree()` wrappers in `XlsxSharedStrings.cs` by passing the main reader directly to `ReadStringItem` and utilizing depth-based matching to stop sequentially.

## [2.2.0] - 2026-05-23

AI/ML data-pipeline release: JSONL becomes a first-class format alongside CSV, Fixed-Width, and Excel, with CSV-to-JSONL fine-tuning converters, embedding-API batching, and an inline vector parser. The UTF-8 read path gains a dedicated AVX-512 SIMD implementation that now outpaces Sep, and native-AOT publish is validated end-to-end in CI. Backward compatible with 2.1.x.

### Added
- **JSONL (JSON Lines) read / write — new format.** `Jsonl.Read<T>()` and `Jsonl.Write<T>()` mirror the existing `Csv` / `Excel` fluent builder pattern: `WithJsonOptions`, `WithTypeInfo` (AOT), `SkipEmptyLines`, `WithMaxLineSize` (1 MiB default DoS cap), `WithMaxRowCount`, `SkipRows`, `WithProgress`, `WithValidationMode`, `OnError`, with terminals `FromText` / `FromFile` / `FromStream` / `FromFileAsync` / `FromStreamAsync` / `FromPipeReaderAsync` and the corresponding write-side `ToText` / `ToFile` / `ToStream` / `ToFileAsync` / `ToStreamAsync`. Async streaming uses `System.IO.Pipelines` for line splitting on `\n` (transparently handles `\r\n`, strips UTF-8 BOM). Reflection paths carry `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`; `WithTypeInfo(JsonTypeInfo<T>)` provides a fully trim/AOT-safe path. Sync helpers `Jsonl.DeserializeRecords<T>` / `WriteToText<T>` / `WriteToFile<T>` / `WriteToStream<T>` and async `DeserializeRecordsAsync<T>` / `WriteToFileAsync<T>` / `WriteToStreamAsync<T>` round out the static surface. New `JsonlException` + `JsonlErrorCode` (`LineTooLong`, `TooManyRows`, `InvalidOptions`, `DeserializeError`, `SerializeError`). New `docs/jsonl.md` API reference.
- **`Jsonl.CreateDataReader`** — `DbDataReader` adapter for `SqlBulkCopy.WriteToServer` and any tool that consumes `IDataReader`. Schema inferred from the first non-empty line, or explicit via `JsonlDataReaderOptions.Columns` carrying `JsonlColumnDefinition(Name, JsonPath, DataType)`. JSONPath syntax supports dotted keys and bracket-indexed arrays (`key.key[idx].key`); missing keys produce `DBNull`. Hand-rolled JSONPath evaluator over `JsonElement` — no reflection, no annotations needed.
- **`CsvToJsonlConverter` — CSV → JSONL in one call.** `Convert(string|path|stream, CsvToJsonlShape, options?)` + `ConvertAsync`. Three shape descriptors ship: `CsvToJsonlShape.FlatObject()` (one JSON property per column), `CsvToJsonlShape.OpenAiChat(systemColumn, userColumn, assistantColumn)` (OpenAI fine-tuning chat shape), `CsvToJsonlShape.AnthropicMessages(userColumn, assistantColumn)`. Shape descriptors carry no provider SDK and make no network calls — they only describe the JSON layout. Reuses the existing CSV row reader so conversion is allocation-light on the parsing side. New `CsvToJsonlOptions` (delimiter, header row, newline, encoder).
- **`JsonlToCsvConverter` — reverse direction.** `Convert(string|path, options?)`. Infers the CSV column union from the first `SchemaInferencePeekRows` JSONL records (default 100); nested objects/arrays are emitted as JSON-encoded strings in their cell.
- **`BatchAsync(int size)` async extension.** `HeroParser.Streaming.AsyncEnumerableExtensions.BatchAsync<T>(this IAsyncEnumerable<T>, int, CancellationToken)` groups any async sequence into fixed-size `IReadOnlyList<T>` batches. Built for embedding-API pipelines (OpenAI / Voyage / Cohere / Anthropic typically want 100–2048-row requests). Final partial batch yielded if the source ends mid-batch; `size <= 0` throws `ArgumentOutOfRangeException`. Works against `Csv.Read<T>().FromFileAsync`, `Excel.Read<T>().FromFileAsync`, and `Jsonl.Read<T>().FromFileAsync` alike.
- **`VectorParser` — inline embedding columns.** `HeroParser.Vectors.VectorParser.ParseFloats(span)` / `ParseDoubles(span)` plus `TryParseFloats` / `TryParseDoubles` (with optional `CultureInfo`). Accepts `[0.1,0.2,0.3]`, `0.1,0.2,0.3`, `0.1 0.2 0.3`, and `[]` (empty). Culture-aware: when the supplied culture uses comma as the decimal separator, comma is treated as the decimal mark and `;` / whitespace become the value separators. Pre-computed embedding vectors are the common AI/ML dataset shape this targets.
- **`examples/HeroParser.Examples.AiPipeline`** — runnable end-to-end demo: load Q/A CSV → emit OpenAI fine-tuning JSONL via `CsvToJsonlConverter` → stream it back via `Jsonl.Read<ChatExample>().FromFileAsync(...).BatchAsync(2)` → print batched record counts. Added to `HeroParser.slnx`.
- **JSONL benchmark.** `benchmarks/HeroParser.Benchmarks/JsonlBenchmark.cs` — round-trip read / write of flat-object JSONL records under `MemoryDiagnoser` for 10k and 100k rows, sync and async variants.
- **Coverage push to 90.31%.** Whole-library line coverage went from 84.84% to 90.31% (1058 net new covered lines). Added 35 waves of targeted test files under `tests/HeroParser.Tests/CoveragePushTests*.cs` (~750 new tests) exercising previously uncovered paths in CSV parsing, fixed-width binders, Excel writers, multi-schema dispatch, delimiter detection, validation pipelines, and the new JSONL surface. Several "untestable" SIMD branches turned out to be testable via the internal `HardwareCapabilities.Override` test seam (AsyncLocal overrides for AVX-2 / AVX-512BW / PCLMULQDQ); waves 16–17 force the scalar / AVX-2-only / non-PCLMULQDQ fallbacks under a `[Collection("HardwareCaps")]` xUnit collection so the global overrides are serialized.
- **Excel AOT compatibility tests.** `tests/HeroParser.AotTests/Tests/ExcelAotTests.cs` exercises `Excel.Write<T>()` and `Excel.Read<T>()` under `PublishAot=true` for the first time: generated writer output, generated reader parsing, round-trip integrity, `[TabularMap]` index/name handling, multiple generated types in the same binary, and nullable value columns. Complements the existing CSV and Fixed-Width AOT coverage.
- **Write-path generator-vs-reflection benchmark.** `benchmarks/HeroParser.Benchmarks/WriteGeneratorBenchmarks.cs` isolates the source-generated template writer from the reflection-based writer for both CSV and Fixed-Width by using two record types of identical shape (one with `[GenerateBinder]`, one without). Quantifies the throughput and allocation delta a user buys by adding the attribute.
- **CI: native AOT publish + run gate.** `ci.yml` now runs `dotnet publish --runtime linux-x64` on the AOT test project (ubuntu-latest + net10.0) and executes the resulting native binary. Previously CI only ran `dotnet run` (JIT), so IL2070 / IL2090 trim warnings and ILC codegen failures were invisible. This closes the feedback loop — future reflection regressions will fail CI rather than hide until a user hits them at publish time.

### Performance
- **UTF-8 AVX-512 read path now outpaces Sep.** Added a dedicated AVX-512 (64-byte chunk) and AVX2 (32-byte chunk) UTF-8 SIMD scanner in `CsvRowParser.cs`, allocation-free `ArrayPool` adapters across `CsvCharToByteBinderAdapter` and `CsvRecordBinderFactory`, and a UTF-8 byte path in the source-generated binder (`CsvRecordBinderGenerator`). On the 10k-row x 25-column reading benchmark (AMD Ryzen AI 9 HX PRO 370, .NET 10) HeroParser UTF-8 (`byte[]`) runs at **0.88x Sep's time — ~12% faster** on both quoted and unquoted data, at a fixed 112-byte allocation. **UTF-8 (`byte[]`) is now the recommended read API**; UTF-16 (`string`) stays within ~13% of Sep at the same 112-byte allocation.
- **Optimized UTF-16 SIMD CSV parsing loop**: Redesigned `TrySimdParseUtf16` in `CsvRowParser.cs` to process 32-character (AVX2) and 64-character (AVX-512) chunks at a time using unsigned saturation packing (`PackUnsignedSaturate`) and lane permutations (`Permute4x64`), resulting in a major performance boost (within 12-13% of the highly optimized competitor `Sep`) while maintaining a completely allocation-free (112 bytes) reading hot path.
- **Register-based slow path**: Replaced memory-accessing `Unsafe.Add` operations in `CsvRowParser.cs`'s masking loop with zero-memory-reload bitwise register checks, maximizing CPU cache and memory bandwidth during quote/delimiter tracking.
- **Excel zero-allocation shared string parsing**: Refactored `ReadStringItem` in `XlsxSharedStrings.cs` to bypass expensive subtree XML readers (`XmlReader.ReadSubtree()`), replacing it with direct sequential stream scanning to dramatically reduce GC pressure when parsing large spreadsheets.

### Fixed
- **AOT publish is now clean for the library.** A first-ever `dotnet publish -r linux-x64` of the AOT test project surfaced six pre-existing trim-analysis errors (IL2026 / IL2070 / IL2090) on the reflection-fallback paths that had been silent because CI only exercised the JIT path. Annotated each offending helper — `CsvRecordWriter.BuildAccessors`, `CsvRecordWriter.CreateGetter`, `FixedWidthRecordWriter.BuildFieldDefinitions`, `FixedWidthRecordWriter.BuildGetter`, `ExcelRecordWriter.BuildAccessors`, `ExcelRecordWriter.CreateGetter`, `FixedWidthRecordBinder.CreateTemplatesFromReflection`, `FixedWidthRecordBinder.BuildTemplates` — with `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` so the annotation chain is honest end-to-end. Added the same annotations to the reflection constructor of `FixedWidthRecordWriter` (they were missing; CSV and Excel constructors already had them). Suppressed IL2026/IL3050 on `FixedWidthRecordBinder.Bind` with a justification comment matching the `CsvRecordWriterFactory.GetWriter<T>` / `FixedWidthRecordWriterFactory.GetWriter<T>` pattern (reflection fallback only taken when no `[GenerateBinder]` is registered for T).

### Changed
- **Factory fallback annotations.** `CsvRecordWriterFactory.GetWriter<T>` and `FixedWidthRecordWriterFactory.GetWriter<T>` carry an `[UnconditionalSuppressMessage]` for `IL2026` and `IL3050` with a justification comment explaining that the reflection branch is only taken when no `[GenerateBinder]` is registered for `T`. Users decorating their types with `[GenerateBinder]` no longer see trimming/AOT warnings propagate from these factory methods.
- **NuGet dependencies refreshed.** `Microsoft.SourceLink.GitHub` 8.0.0 → 10.0.203 (build-time only, `PrivateAssets=all`), `Sep` 0.12.1 → 0.12.5 (benchmarks, improves comparison accuracy), `Microsoft.NET.Test.Sdk` 18.0.1 → 18.4.0, `xunit.v3` 3.2.0/3.2.1 → 3.2.2 (unifies the previously inconsistent versions across the two test projects), `Microsoft.CodeAnalysis.CSharp` 5.0.0 → 5.3.0, `coverlet.collector` 6.0.0 → 10.0.0 (major version bump aligning coverlet with .NET release cadence; coverage collector only). Runtime-facing dependency `System.IO.Pipelines` deliberately kept at 8.0.0 — it ships transitively to consumers and 8.0.0 is the correct floor version for a library that multi-targets `net8.0`.

## [2.1.3] - 2026-04-24

Documentation and CI hardening release. No runtime code changes — functionally identical to 2.1.2.

### Fixed
- **Assembly / package version drift in the release workflow.** `create-release.yml` passed `-p:Version` only to the `dotnet pack --no-build` step, not to the preceding `dotnet build`. The result: the shipped DLL was stamped with whatever `<Version>` happened to be in `HeroParser.csproj` (often stale), while the `.nupkg` was labeled with the release input. Anyone calling `Assembly.GetName().Version` or reading `FileVersionInfo.FileVersion` would see a number that disagreed with the NuGet package version. The `dotnet build` step now also receives `-p:Version=${{ inputs.version }}`, so assembly metadata and package metadata can never diverge.

### Changed
- **GitHub Actions hardened.** Every external `uses:` in `.github/workflows/*.yml` and `.github/actions/*` pinned to an immutable 40-character commit SHA with a `# <version>` comment. Floating `@v4` / `@v5` / `@v6` tags can no longer be silently repointed by an upstream compromise.
- **GitHub Actions bumped to latest stable**: `actions/checkout` v6.0.2, `actions/setup-dotnet` v5.2.0, `actions/upload-artifact` v7.0.1, `actions/download-artifact` v8.0.1, `actions/github-script` v9.0.0 (major — Node 24 runtime), `actions/attest-build-provenance` v4.1.0, `actions/dependency-review-action` v4.9.0, `codecov/codecov-action` v6.0.0 (major), `EnricoMi/publish-unit-test-result-action` v2.23.0, `NuGet/login` v1.1.0.

### Documentation
- Added `docs/migration-v1-to-v2.md` — attribute rename map from 1.x to 2.x (`[CsvColumn]` → `[TabularMap]` + `[Validate]`, `[FixedWidthColumn]` → `[PositionalMap]`, etc.), before/after samples for CSV / Fixed-Width / combined records, behavioral callouts, and a mechanical migration recipe. Written for anyone still on 1.x; was missing at the time of the 2.0.0 release.
- Added repository development guidelines (`CLAUDE.md`, `AGENTS.md`) covering project structure, build commands, code style, performance lessons, and architecture overview.
- Swept `docs/csv.md` and `docs/fixed-width.md` to replace lingering v1 attribute references (`[CsvColumn]`, `[CsvGenerateBinder]`, `[FixedWidthColumn]`) with current v2 names. Removed stale "legacy X" transition notes now that the legacy attributes are gone.
- Restructured this CHANGELOG with per-version entries for 2.0.0, 2.1.0, 2.1.1, and 2.1.2 (previously all lumped under `Unreleased`).

## [2.1.2] - 2026-03-17

### Added
- Comprehensive unit and integration tests for Excel reading and writing features.

### Changed
- QA skill validation: checks for `qa-only SKILL.md` and enhanced skill-structure validation.

## [2.1.1] - 2026-03-16

### Changed
- README and NuGet package description enhanced to reflect Excel support.
- Minor validation-handling refactor in `ExcelRecordWriter` and `CsvRecordBinderGenerator`.

## [2.1.0] - 2026-03-16

### Added
- **Validation mode for record reading**: `ValidationMode.Strict` (default) and `ValidationMode.Lenient`. Strict throws on first validation failure; lenient collects errors and continues.
- **Write-side validation**: `[Validate]` rules now enforced for Excel and Fixed-Width writers. Previously only read paths ran validation; writers could emit records that would fail to read back.
- **`ExcelReadOptions`** with validation enforcement controls.

### Performance
- **`XlsxWriter` zero-allocation cell writing**: restructured the cell emission path to avoid per-cell allocations.

### Changed
- Enhanced XML documentation and access modifiers for `ExcelRecordWriter` and `XlsxWriter`.

### Tests
- `CsvWriteValidationTests` for CSV write-side validation scenarios.
- Comprehensive Excel write tests covering quoting, formatting, multi-sheet, progress, and error handling.

## [2.0.0] - 2026-03-14

### Breaking Changes

- **Unified attribute model.** The format-specific attributes from 1.x are removed and replaced by six concern-separated attributes that work across CSV, Excel, and Fixed-Width. See [docs/migration-v1-to-v2.md](docs/migration-v1-to-v2.md) for the rename map and mechanical migration recipe.
  - Removed: `[CsvColumn]`, `[FixedWidthColumn]`, `[CsvGenerateBinder]`, `[FixedWidthGenerateBinder]`, `[FixedWidthRequired]`, `[FixedWidthStringLength]`, `[FixedWidthRange]`, `[FixedWidthRegex]`.
  - Added: `[GenerateBinder]`, `[TabularMap]`, `[PositionalMap]`, `[Parse]`, `[Format]`, `[Validate]`.
- **HERO008 build diagnostic.** When `[GenerateBinder]` is applied, every `[TabularMap]` must specify either `Name` or `Index`. Omitting both is a hard build error (was a silent fallback in 1.x).

### Added

#### Excel (.xlsx) reading — new in 2.0
- **Read**: `Excel.Read<T>()` fluent builder — sheet selection, culture, validation, progress, error handling. Zero extra dependencies (uses only `System.IO.Compression` + `System.Xml`).
- **Read variants**: `FromSheet(name|index)`, `AllSheets()` returning `Dictionary<string, List<T>>`, multi-sheet different-type reads via `Excel.Read().WithSheet<T>(name)`.
- **Row-level read**: `Excel.Read().FromFile(...)` for untyped row iteration via `ExcelRowReaderBuilder`.
- **DataReader**: `Excel.CreateDataReader(stream|path)` — `DbDataReader` for streaming into `SqlBulkCopy` and ADO.NET consumers.
- **XLSX infrastructure**: `XlsxReader`, `XlsxWorkbook` (sheet name/index mapping), `XlsxSheetReader` (streaming rows), `XlsxStylesheet` (date-format detection), `XlsxSharedStrings` (shared-string table), `XlsxRowAdapter` bridge, `ExcelException`, `ExcelReadOptions`, `ExcelProgress`, `XlsxCellType`.

#### Unified attribute system
- **`[GenerateBinder]`** — single source-generator trigger for CSV, Fixed-Width, and Excel record binding (replaces `[CsvGenerateBinder]` + `[FixedWidthGenerateBinder]`).
- **`[TabularMap(Name, Index)]`** — column mapping for CSV and Excel.
- **`[PositionalMap(Start, Length, End, PadChar, Alignment)]`** — position mapping for Fixed-Width.
- **`[Parse(Format)]`** — read-side type conversion.
- **`[Format(WriteFormat, ExcludeIfAllEmpty)]`** — write-side formatting and empty-column pruning.
- **`[Validate(NotNull, NotEmpty, MinLength, MaxLength, RangeMin, RangeMax, Pattern, PatternTimeoutMs)]`** — cross-format field validation.
- Compile-time diagnostics `HERO004`–`HERO008` for invalid attribute usage.

### Changed
- **Attribute handling simplified** in CSV and Fixed-Width record binder generators.
- **Excel XML parsing hardened**, shared code deduplicated across the Excel read path.

### Fixed
- v2 release gaps: duplicate type guard, dead type removal, shortcut API cleanup.

## [1.x] — 2025-11 through 2026-03

Pre-2.0 releases (1.0.0 through 1.7.1) added, incrementally, everything from the 1.0 foundation to the 1.x feature set that was carried into 2.0: CSV writing with RFC 4180 compliance, SIMD-accelerated writing (AVX2/SSE2), fluent reader/writer builders (`Csv.Read<T>()`, `Csv.Write<T>()`), non-generic builders, `CsvAsyncStreamWriter`, Fixed-Width read and write (`FixedWidth.Read<T>()` / `Write<T>()` with `[PositionalMap]`, alignment, padding, `AllowShortRows`, `MaxRecordCount`, `MaxInputSize`, async streaming), LINQ-style extensions on CSV readers, CSV injection protection, source line number tracking (`CsvRow.SourceLineNumber`), source-generated multi-schema dispatch, delimiter auto-detection, CSV structural validation, schema inference, CSV↔Fixed-Width converters, PipeReader integration, `IDataReader`/`DbDataReader` bridges, CLMUL-based branchless quote tracking (PCLMULQDQ), `ArrayPool<T>` buffer reuse, `stackalloc` fast paths for small column counts, and DoS-resistant stream reader buffer caps.

Per-version detail for 1.1.0 through 1.7.1 lives in the GitHub release notes: <https://github.com/KoalaFacts/HeroParser/releases>. This CHANGELOG was not maintained during that period; it will be kept current from 2.0.0 forward.

## [1.0.0] - 2025-11-20

- Added configurable RFC compliance options (newlines-in-quotes opt-in, ability to disable quote parsing for speed).
- Expanded parsing helpers: culture-aware and format overloads for date/time types; enum parsing; numeric helpers across byte/short/int/long/float/decimal; timezone parsing.
- Improved UTF-8 parsing consistency and clarified allocation behavior (UTF-8 culture/format parsing decodes to UTF-16).
- Updated CI permissions for publishing test results; benchmarks now toggle RFC options for comparison.
