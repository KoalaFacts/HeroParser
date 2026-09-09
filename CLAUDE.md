# HeroParser Development Guidelines

## Active Technologies
- C# with multi-framework targeting (net8.0, net9.0, net10.0) + BenchmarkDotNet (performance validation), Source Generators (allocation-free mapping), Minimal dependencies (only System.IO.Pipelines, System.IO.Compression for Excel)

## Project Structure
```
src/
  HeroParser/                  # Core library (CSV + Fixed-Width + Excel parsing/writing)
  HeroParser.Generators/       # Source generators (netstandard2.0)
tests/
  HeroParser.Tests/            # Unit and integration tests
  HeroParser.Generators.Tests/ # Source generator tests
  HeroParser.AotTests/         # AOT compatibility tests
benchmarks/
  HeroParser.Benchmarks/       # BenchmarkDotNet perf tests (vs Sep 0.17.0)
.github/
  workflows/                   # CI, benchmarks, security scan, release, NuGet publish
```

## Commands
```bash
# Build all projects
dotnet build

# Run unit tests
# Tests run on Microsoft.Testing.Platform, so runner arguments go after `--`
# and use MTP names (--filter-trait, not --filter).
dotnet test tests/HeroParser.Tests -- --filter-trait Category=Unit

# Run integration tests
dotnet test tests/HeroParser.Tests -- --filter-trait Category=Integration

# Run all tests
dotnet test

# Run source generator tests only
dotnet test tests/HeroParser.Generators.Tests

# Run AOT compatibility tests (uses dotnet run, not dotnet test)
dotnet run --project tests/HeroParser.AotTests -c Release

# Check code formatting
dotnet format --verify-no-changes

# Run benchmarks
dotnet run -c Release --project benchmarks/HeroParser.Benchmarks
```

**CI notes**: CI builds in `Release` configuration across a matrix of 3 OSes (ubuntu, windows, macos) and 3 frameworks (net8.0, net9.0, net10.0). Code coverage is collected on ubuntu/net10.0 only.

**Test platform**: The test projects run on Microsoft.Testing.Platform rather than VSTest. Practical consequences:
- `global.json` selects the runner (`"test": { "runner": "Microsoft.Testing.Platform" }`); without it `dotnet test` falls back to VSTest and fails on the .NET 10 SDK.
- Test projects must set `<OutputType>Exe</OutputType>` — MTP test assemblies are executables. `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and `coverlet.collector` are VSTest-only and are no longer referenced.
- Runner arguments follow `--`: `--filter-trait` (not `--filter`), `--report-trx --report-trx-filename` (not `--logger trx`), `--coverage --coverage-output-format cobertura` (not `--collect "XPlat Code Coverage"`).
- Each test binary is also directly runnable and accepts xunit's native single-dash options, which is handy for debugging: `./HeroParser.Tests -trait Category=Unit -list traits`.

**Every test needs a Category trait**: CI runs this assembly once per category, so a test with no `Category` trait is discovered and reported but never executed by any job — green precisely because it never ran. Mark each test `[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]` or `INTEGRATION`, or put the trait on the class when the whole class is one category. `TestCategoryCoverageTests` enforces this and fails with the offending method names, so an untagged test breaks the build rather than disappearing.

## Code Style
- **Nullable reference types**: Enabled (`<Nullable>enable</Nullable>`)
- **Language version**: Latest (`<LangVersion>latest</LangVersion>`)
- **Implicit usings**: Enabled
- **File-scoped namespaces**: Preferred (suggestion severity — won't fail build, but used consistently throughout)
- **Private fields**: `camelCase` (no `s_` prefix for static fields, no `_` prefix)
- **Constants**: `UPPER_CASE_WITH_UNDERSCORES`
- **Interfaces**: `IInterfaceName` (prefix `I`, PascalCase)
- **Types, Properties, Methods**: `PascalCase`
- **`var` usage**: At discretion (IDE0008 disabled)
- **Braces**: Optional for single-line blocks (IDE0011 disabled)
- **Build enforces code style**: `EnforceCodeStyleInBuild = true`, `TreatWarningsAsErrors = true`
- **Collection initialization**: Use modern syntax (IDE0300-0305 enforced as warnings)
- **Formatting**: Must pass `dotnet format --verify-no-changes` in CI
- **XML docs**: Required for public API members in `src/` (`CS1591` is warning-level; suppressed for test/benchmark projects)
- **NuGet lock files**: `RestoreLockedMode` is enabled. When adding/updating packages, run `dotnet restore --force-evaluate` to regenerate lock files
- **Dependency policy**: GPL-2.0, GPL-3.0, and AGPL-3.0 licenses are denied. High-severity vulnerabilities fail CI

## Unified Attribute System (v2)
HeroParser v2 uses concern-separated attributes instead of format-specific ones:
- **`[TabularMap(Name, Index)]`** — column mapping for CSV/Excel
- **`[PositionalMap(Start, Length, End, PadChar, Alignment)]`** — position mapping for FixedWidth
- **`[Parse(Format)]`** — read-side type conversion (e.g., date format)
- **`[Validate(NotNull, NotEmpty, MaxLength, MinLength, RangeMin, RangeMax, Pattern)]`** — bidirectional validation
- **`[Format(WriteFormat, ExcludeIfAllEmpty)]`** — write-side formatting
- **`[GenerateBinder]`** — triggers source generator for the class (replaces `[CsvGenerateBinder]` and `[FixedWidthGenerateBinder]`)

Convention-based mapping: unmarked properties default to `TabularMap(Name = propertyName)` for CSV/Excel. FixedWidth requires explicit `[PositionalMap]`.

A single record can have both `[TabularMap]` and `[PositionalMap]` to be read from CSV, Excel, and FixedWidth.

## Excel (.xlsx) Reading and Writing
Read and write Excel files with zero extra dependencies (uses `System.IO.Compression` + `System.Xml`):
```csharp
// --- Reading ---
// Typed record reading
var records = Excel.Read<MyRecord>().FromFile("data.xlsx");
var records = Excel.Read<MyRecord>().FromSheet("Sheet2").FromStream(stream);

// Row-level reading (string arrays)
var rows = Excel.Read().FromFile("data.xlsx");

// All sheets (same type)
var dict = Excel.Read<MyRecord>().AllSheets().FromFile("data.xlsx");  // Dictionary<string, List<T>>

// Multi-sheet (different types)
var result = Excel.Read()
    .WithSheet<OrderRecord>("Orders")
    .WithSheet<CustomerRecord>("Customers")
    .FromFile("data.xlsx");

// DataReader for database bulk loading
using var reader = Excel.CreateDataReader("data.xlsx");

// --- Writing ---
// Typed record writing (fluent builder)
Excel.Write<MyRecord>().ToFile("out.xlsx", records);
Excel.Write<MyRecord>().WithSheetName("Sheet1").ToStream(stream, records);

// Static convenience
Excel.WriteToFile("out.xlsx", records);
Excel.WriteToStream(stream, records);
byte[] bytes = Excel.SerializeRecords(records);

// Async variants (IEnumerable or IAsyncEnumerable)
await Excel.Write<MyRecord>().ToFileAsync("out.xlsx", records);
await Excel.Write<MyRecord>().ToStreamAsync(stream, records);
await Excel.WriteToFileAsync("out.xlsx", records);

// Multi-sheet output
Excel.WriteMultiSheet()
    .WithSheet("Orders", orderRecords)
    .WithSheet("Customers", customerRecords)
    .ToFile("out.xlsx");
```

Key files in `src/HeroParser/Excels/` (internal namespace uses `HeroParser.Excels` to avoid conflict with the `Excel` static class).

## Multi-Schema CSV Parsing
Supports mapping rows to different record types via a discriminator column.
- **Runtime**: Flexible, uses `WithMultiSchema().WithDiscriminator("Type")`.
- **Source-Generated**: Optimal performance (`[CsvGenerateDispatcher]`). 2x faster than runtime.
Key files in `src/HeroParser/SeparatedValues/Records/MultiSchema/`.

## Delimiter Detection & CSV Validation
- **Delimiter Detection**: `CsvDelimiterDetector.DetectDelimiter()` analyzes sample rows to identify delimiter (`,`, `;`, `|`, `\t`). Returns confidence score 0-100. Supports UTF-8 and UTF-16 input.
- **CSV Validation**: `Csv.Validate()` checks structural integrity - consistent column counts, required headers, row limits, empty files. Auto-detects delimiter if not specified.
Key files in `src/HeroParser/SeparatedValues/Detection/` and `src/HeroParser/SeparatedValues/Validation/`.

## IDataReader Support
CSV, Fixed-Width, and Excel support `DbDataReader` for streaming large files into databases.
- `Csv.CreateDataReader(stream|path)` - CSV streaming reader
- `FixedWidth.CreateDataReader(stream|path)` - Fixed-width streaming reader
- `Excel.CreateDataReader(stream|path)` - Excel streaming reader
- Supports header mapping, null value detection, column name overrides, case-insensitive headers.

## Code Quality Rules

### Never Suppress Warnings to Bypass Issues
- **NEVER use `#pragma warning disable` to suppress warnings that indicate real issues**
- Fix the underlying problem instead of hiding it
- Acceptable suppressions are only for:
  - False positives with clear justification comments
  - Intentional API design decisions (e.g., `IDE0060` for API compatibility where parameter is intentionally unused)
  - Framework limitations that cannot be resolved (e.g., `IsExternalInit` for older frameworks)
- For xUnit tests: Use `TestContext.Current.CancellationToken` instead of suppressing `xUnit1051`
- For lambdas: Prefer method groups over trivial lambdas (e.g., `reports.Add` instead of `p => reports.Add(p)`) to satisfy `IDE0200`
- Always include a justification comment explaining WHY the suppression is acceptable

### Avoid ToString() Allocations in Hot Paths
- Use `ReadOnlySpan<char>` or `ReadOnlySpan<byte>` comparisons instead of converting to strings
- For integer discriminator keys, compare numeric values directly rather than calling `ToString()`
- Prefer `stackalloc` or `ArrayPool` over heap allocations in tight loops

### Minimum CodeQL / Static-Analysis Standards (always follow)
These are the patterns CodeQL flags as `cs/useless-assignment-to-local` and
`cs/local-not-disposed`. Apply them everywhere — `src/`, `tests/`,
`benchmarks/`, and example projects:

1. **Dispose every locally-created `IDisposable` deterministically.** Use
   `using var x = new T()` (or `await using var` for `IAsyncDisposable`)
   instead of bare `var x = new T()`. This includes `CancellationTokenSource`
   — even in tests where the process is about to exit.
2. **For locally-created streams in `IAsyncEnumerable` methods, prefer
   `await using` at the declaration site** rather than disposing in a
   `finally` block. CodeQL cannot trace disposal through iterator state
   machines, and the `await using` form also guarantees disposal if a later
   constructor (e.g. `PipeReader.Create`) throws before the `try` block.
3. **Never write `var _ = expr;`.** If the value is genuinely discarded,
   write `_ = expr;` (no `var`). If the value is used, name it.
4. **Never write `foreach (var _name in source)` for an unused loop
   variable.** Use `foreach (var _ in source)` — the discard pattern.
5. **Don't write redundant null assertions after the variable has already
   been dereferenced.** `Assert.True(x is not null)` *after* `x.ToList()`
   tells CodeQL that `x` could have been null at the prior dereference; it
   either adds nothing (the dereference would have NRE'd) or it masks the
   real bug. Assert on the actual outcome (`Assert.Equal(5, result.Count)`).

## Performance Optimization Lessons

### How to measure

- **Only the same-runner A/B counts.** The PR benchmark job builds the PR base in a worktree and runs `--vs-sep-reading` for base and head back to back, posting both under "Sep Comparison, same runner" in the PR comment. GitHub-hosted runners land on different CPUs from run to run (EPYC 7763 Zen 3 without AVX-512, EPYC 9V74 Zen 4, Xeon 6973P-C all seen within a week), so numbers from different runs, from the published docs, or from a laptop are not comparable. Sep's own number should agree within a few percent between the two halves; if not, the runner was unstable and the run should be repeated.
- Local laptop runs are too noisy for effects under ~20%: Sep drifted 25% between consecutive local runs while the same-runner job resolved a 3% change. The same-runner A/B caught a 14% regression that three local trials had read as an improvement.
- Cheap way to test a per-row hypothesis: sweep the row/column split at constant cell count (50000x5, 10000x25, 2500x100). If the ratio to Sep tracks row count, the cost is per-row, not per-byte.

### What Works

- **Scan-ahead row batches** (`CsvRowBatchScanner`): one SIMD pass fills a pooled `ends` buffer with many rows' column ends (ends-only encoding, absolute offsets, a sentinel per row) and `MoveNext` only advances an index. Per-row entry into the parser was ~58 ns against Sep's ~15 ns and was the entire gap at 5-25 columns.
- **Inline "quotes but no line ending" chunk path**: most chunks in a quoted row have quotes and no newline. Handling them in the dispatch (CLMUL mask, filter, bare append, flip parity) instead of calling the state machine is what made the quoted cases win. A non-inlined call per chunk cancels per-row savings.
- **UTF-16 pack-and-saturate**: pack two `short` vectors into one byte vector with `PackUnsignedSaturate` plus a lane permute, then reuse the byte dispatch. Halves compares per element; loads are identical either way. (An older note said this was abandoned for memory traffic; the same-runner A/B showed -20% unquoted, -15% quoted.) Requires ASCII specials so saturated chars (0xFF / 0x00) can never alias them.
- **64-element chunks on AVX2 for the quoted path** (`Avx2PairLanes`): two 256-bit vectors per chunk halve the per-chunk cost (mask extraction, dispatch, CLMUL) where chunks are dispatched one at a time. Same runner: quoted -13% UTF-16 / -10% UTF-8 on EPYC 7763 (native AVX2), -12% / -13% on EPYC 9V74 with AVX-512 disabled; unquoted unchanged. Selected per quote policy in `Scan`; see "What Doesn't Work" for why the unquoted block keeps single vectors.
- **Vector tail scan**: the elements that do not fill a chunk are copied into a zero-padded stack buffer and scanned with the same compares as a full chunk; zero never equals a special, and the UTF-16 pack turns padding into 0x00.
- **Bare delimiter loops on true locals, count by value**: `Unsafe.Add(ref endsBase, endsCount++) = base + bit` with the count passed *by value* into the inlined append and returned, never by `ref`. An address-taken local is kept in memory by the JIT even after inlining: the Zen 3 profile showed twelve instructions per delimiter with a stack reload, increment, store and bounds check, a memory-carried dependency that Zen 3 pays store-forwarding latency for on every delimiter while Zen 4 and the Xeons hide it. Passing the count by value made the loop register-only: Zen 3 unquoted -18% UTF-8, -12% UTF-16 (same instance, interleaved), from 1.10-1.16x Sep to 0.90-1.01x. The per-chunk reserve makes the unchecked write safe.
- **Profile on the CPU that matters** (`profile-zen3.yml`, manual dispatch): fans out jobs, keeps the ones whose runner matches the requested CPU, samples the driver under `perf`, resolves samples against the perf map and dumps the hot methods' code from `/proc/<pid>/mem` for a per-instruction listing (perf's own JIT support cannot see the runtime's code heap). With `compare_ref` it also interleaves base and head timings on that instance: an on-demand A/B for a chosen CPU instead of the runner lottery.
- **Delegated error diagnostics**: when the scanner detects a violation it stops and the reader re-parses that row with `ParseRow`, so exceptions, messages and positions stay byte-identical without duplicating diagnostics code.
- **Pooled buffers behind a class** (`PooledColumnEnds`, `CsvRowBatchCursor`): the reader struct is copied by `foreach` and disposed per copy; a class with an idempotent return prevents double-returning arrays to `ArrayPool`, which otherwise hands the same array to two owners.
- **CLMUL-based quote handling**: PCLMULQDQ prefix XOR for branchless in-quotes masks.
- **Compile-time specialization**: `TQuotePolicy`, `TTrack` and the element type let the JIT eliminate dead paths.
- **ArrayPool for buffer reuse** and **stackalloc for small arrays** in the binders.

### What Doesn't Work

Attempted optimizations that caused regressions, all caught by the same-runner A/B:

1. **Early exit on a combined mask**: the unquoted block loop tested `d0 | le0` and broke whenever vector 0 held any delimiter, which on dense CSV is always, so the whole block's loads and compares were discarded and redone. Combine masks before a branch only when the combined event is rare.

2. **Per-delimiter stores through a `ref` parameter in the block path**: routing the no-newline block through a helper that stored `currentStart` per delimiter and did a popcount per vector regressed unquoted 10k x 25 by 14% (540 us to 617 us). Keep validation on a cold path behind one `HasValue` branch per block.

3. **Batch validation with PopCount** per vector adds overhead over one check per block.

4. **Unsafe.Add for columnEnds writes**: the JIT already eliminates bounds checks it can prove.

5. **Hoisting maxFieldLength checks** into a boolean costs more than the nullable check.

6. **Vector pairs in the unquoted 4-chunk block on AVX2**: four pair chunks plus their line-ending compares plus the broadcast constants exceed the sixteen 256-bit registers, and the spills cost +41% (UTF-8) to +49% (UTF-16) unquoted with AVX-512 disabled on EPYC 9V74, while the same pairs gained 12-13% on the quoted path. Width is chosen per quote policy; keep register pressure in mind before widening any unrolled block.

8. **Vector pairs in a two-chunk unquoted block on AVX2** (PR #90): same bytes and register footprint as the four-chunk single-vector block, chunks dispatched at 64 elements. Neutral (+1-2%, noise) on an EPYC 9V74 VM running x86-64-v3. Per-chunk overhead is not what limits the unquoted path; its cost is the per-delimiter store loop, which pairing does not change. The Zen 3 gap that motivated it turned out to be the `ref` count in that loop (see "count by value" above): guess less, profile the CPU in question first.

7. **Per-row SIMD entry for short rows**: entering the scanner once per row (context setup, three call levels, tail scan) cost 20-25 ns more per row than the old per-row SIMD on the PipeReader path, 24-28% on 40-byte rows. Batching the pipe reader through the cursor removed the per-row entry and made the path 13-25% faster than before instead (three runners). Structure (per row vs per batch) beats trimming the entry.

**Key insight**: the .NET JIT is very good at simple, idiomatic code. Structure (what runs per row vs per chunk vs per byte, what stays inline) moves the numbers; micro-tricks mostly don't.

### Benchmark Baseline (vs Sep 0.17.0)

Same-runner CI, 10,000 rows x 25 columns, .NET 10, AMD EPYC 9V74 (AVX-512), v2.7.0:

| Case | Sep | HeroParser | Ratio | Allocated |
|---|---|---|---|---|
| UTF-8, unquoted | 663.5 us | 491.4 us | 0.74x | 152 B vs 3,952 B |
| UTF-8, quoted | 1,343.2 us | 898.8 us | 0.67x | 152 B vs 4,048 B |
| UTF-16, unquoted | 663.5 us | 534.4 us | 0.81x | 152 B vs 3,952 B |
| UTF-16, quoted | 1,343.2 us | 984.1 us | 0.73x | 152 B vs 4,048 B |

- **Allocations**: 152 B fixed per span read (one pooled batch holder), regardless of row or column count.
- **UTF-8 vs UTF-16**: both are first-class now; prefer UTF-8 when the data is already bytes to skip the pack step, but `string` input no longer needs conversion for performance.
- **Unicode**: verified for CJK, Arabic, Hangul, accented chars, emoji surrogate pairs, U+FFFF and U+FFFD on both element types.

## Architecture Overview

### SIMD Parsing Pipeline (Read Path)
```
Input (UTF-8 bytes) → BOM detection → Row Scanner (SIMD) → Column Extraction → Binding → Records
                                         │
                                    ┌────┴─────┐
                                    │ AVX-512  │  (64-byte chunks)
                                    │ AVX2     │  (32-byte chunks)
                                    │ NEON     │  (16-byte chunks, ARM)
                                    │ Scalar   │  (fallback)
                                    └──────────┘
```
- **Scan-ahead scanner** (`CsvRowBatchScanner` + `CsvRowBatchCursor`): one SIMD pass records a batch of rows' column ends into a pooled buffer; the cursor drives the batch / refill / fallback protocol for the span readers, the streaming readers (`CsvAsyncStreamReader`, `CsvMultiSchemaStreamingRecordReader`) and the pipe reader (`CsvPipeSequenceReader`, which `ReadFromPipeReaderAsync` wraps), which scan their buffered window and never emit a partial row mid-stream. The pipe reader batches over a single-segment buffer and falls back to its per-row `ReadOnlySequence` path only for rows that straddle segments. UTF-16 chunks are packed to bytes with saturation and share the byte dispatch. Errors are delegated to `ParseRow` for identical diagnostics. Used when the delimiter and quote are ASCII and no comment/escape character is set.
- **Per-row parser** (`CsvRowParser.ParseRow`): the one-row contract used by every reader for configurations the batch scanner declines, by the pipe reader for rows that straddle segments, and by the batched readers to re-parse a flagged row so its exception is the same. Its SIMD work is one `CsvRowBatchScanner.Scan` call in single-row mode (`IsSupportedForRow`: ASCII specials, no escape character; a comment character is fine because the parser handles comment lines first). The scalar loop is the oracle for diagnostics and the path for escape characters, non-ASCII specials and SIMD off. Callers size `columnEnds` with `CsvRowBatchScanner.MinEndsCapacity` or the SIMD path is skipped.
- **Column Extraction**: ends-only `columnEnds[]` (sentinel, delimiter positions, row end); `CsvRow<T>` carries a base offset so it can point into a shared batch buffer.
- **Binding**: `ICsvSourceBinder<TElement, T>` maps columns to record properties. Source-generated binders inline type parsing.
- **UTF-16 binding fallback**: `CsvCharToByteBinderAdapter` converts to UTF-8 via `ArrayPool` + `stackalloc`, then uses the byte path.

### Write Path
```
Records → PropertyAccessor (compiled expression trees) → CsvStreamWriter (buffered) → TextWriter
                                                              │
                                                         Quote analysis (SIMD AVX2/SSE2)
```

### Key Abstractions
- `CsvRowReader<T>` — ref struct row iterator (T = byte or char); batches via `CsvRowBatchCursor`, falls back to `CsvRowParser.ParseRow`
- `CsvRecordReader<TElement, T>` — ref struct that wraps row reader + binder
- `CsvStreamWriter` — buffered writer with `ArrayPool<char>` management
- `CsvAsyncStreamWriter` — async variant with `char[]` + `byte[]` dual buffers
- `ICsvSourceBinder<TElement, T>` — interface for source-generated and reflection binders

## Troubleshooting

### Lock file out of date
```
error NU1004: The packages lock file is inconsistent with the project dependencies
```
**Fix**: Run `dotnet restore --force-evaluate` to regenerate lock files, then commit the updated `packages.lock.json`.

### AOT trim warnings
If `dotnet publish -r linux-x64 --self-contained` produces trim warnings:
- Ensure record types use `[GenerateBinder]` attribute
- Reflection-based binding is annotated with `[RequiresUnreferencedCode]` and will warn under trimming

### SIMD fallback behavior
HeroParser auto-detects CPU capabilities at runtime:
- AVX-512 → 64-byte chunk processing (best throughput)
- AVX2 → 32-byte chunks (common on modern x64)
- NEON → 16-byte chunks (ARM64, e.g., Apple Silicon, AWS Graviton)
- Scalar → byte-by-byte (always works)

Set `CsvReadOptions.UseSimdIfAvailable = false` to force scalar mode for debugging.

### Common CI failures
- **Format check fails**: Run `dotnet format` locally and commit the changes
- **CS1591 (missing XML docs)**: Add `<summary>` XML docs to new public members in `src/`
- **IDE0300-0305 (collection init)**: Use `[1, 2, 3]` syntax instead of `new[] { 1, 2, 3 }`
- **TreatWarningsAsErrors**: Any analyzer warning becomes a build error; fix rather than suppress

### PipeReader integration
For streaming from network sockets or HTTP:
```csharp
await foreach (var row in Csv.ReadFromPipeReaderAsync(pipeReader))
{
    // Process row as it arrives
}
```

### Schema inference
Auto-detect column types from CSV data:
```csharp
var schema = Csv.InferSchema(csvData);
// Returns column names, types (Integer, Decimal, Boolean, DateTime, Guid, String), and nullability
```

### CSV/FixedWidth conversion
```csharp
// CSV → Fixed-Width
var fixedWidth = CsvToFixedWidthConverter.Convert(csv, columns);
// Fixed-Width → CSV
var csv = FixedWidthToCsvConverter.Convert(fixedWidthData, columns);
```

### WebAssembly (WASM) & JS Workspace

#### Build & Compile WASM Engine
Compiles C# assemblies to Webcil-wrapped `.wasm` binary modules and copies them directly to the NPM workspace:
```bash
dotnet publish src/HeroParser.Wasm/HeroParser.Wasm.csproj -c Release
```

#### Compile Browser Playground Demo
Builds the client application bundle using Vite+ and Vue 3.6 Vapor:
```bash
# From workspace root
npm install --prefix npm --legacy-peer-deps
npm run build --workspace=browser-demo --legacy-peer-deps
```

#### Version Synchronization
The version number is centrally defined in `Directory.Build.props`. To propagate bumps to NPM `package.json`, snap recipes, installers, and documentation files, run:
```bash
npm run sync-version --prefix npm
```

