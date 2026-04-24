# Changelog

All notable changes to HeroParser are documented in this file. This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Excel AOT compatibility tests.** `tests/HeroParser.AotTests/Tests/ExcelAotTests.cs` exercises `Excel.Write<T>()` and `Excel.Read<T>()` under `PublishAot=true` for the first time: generated writer output, generated reader parsing, round-trip integrity, `[TabularMap]` index/name handling, multiple generated types in the same binary, and nullable value columns. Complements the existing CSV and Fixed-Width AOT coverage.
- **Write-path generator-vs-reflection benchmark.** `benchmarks/HeroParser.Benchmarks/WriteGeneratorBenchmarks.cs` isolates the source-generated template writer from the reflection-based writer for both CSV and Fixed-Width by using two record types of identical shape (one with `[GenerateBinder]`, one without). Quantifies the throughput and allocation delta a user buys by adding the attribute.

### Changed
- **Factory fallback annotations.** `CsvRecordWriterFactory.GetWriter<T>` and `FixedWidthRecordWriterFactory.GetWriter<T>` carry an `[UnconditionalSuppressMessage]` for `IL2026` and `IL3050` with a justification comment explaining that the reflection branch is only taken when no `[GenerateBinder]` is registered for `T`. Users decorating their types with `[GenerateBinder]` no longer see trimming/AOT warnings propagate from these factory methods.

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
