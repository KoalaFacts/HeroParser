# HeroParser Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-03-06

## Active Technologies
- C# with multi-framework targeting (net8.0, net9.0, net10.0) + BenchmarkDotNet (performance validation), Source Generators (allocation-free mapping), Zero external dependencies for core library

## Project Structure
```
src/
  HeroParser/                  # Core library (CSV + Fixed-Width parsing/writing)
  HeroParser.Generators/       # Source generators (netstandard2.0)
tests/
  HeroParser.Tests/            # Unit and integration tests
  HeroParser.Generators.Tests/ # Source generator tests
  HeroParser.AotTests/         # AOT compatibility tests
benchmarks/
  HeroParser.Benchmarks/       # BenchmarkDotNet perf tests (vs Sep 0.12.1)
.github/
  workflows/                   # CI, benchmarks, security scan, release, NuGet publish
```

## Commands
```bash
# Build all projects
dotnet build

# Run unit tests
dotnet test --filter Category=Unit

# Run integration tests
dotnet test --filter Category=Integration

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

## Recent Changes
- 001-aim-to-be: Added C# with multi-framework targeting (net8.0, net9.0, net10.0) + BenchmarkDotNet (performance validation), Source Generators (allocation-free mapping), Zero external dependencies for core library
- Multi-Schema CSV: Added multi-schema CSV parsing for banking/financial file formats with header/detail/trailer patterns
- UTF-8 Consolidation (Jan 2026): Unified SIMD parsing to UTF-8 only path. UTF-16 string API now converts to UTF-8 internally via optimized `CsvCharToByteBinderAdapter` using ArrayPool and stackalloc.
- Delimiter Detection & CSV Validation (Jan 2026): Added `CsvDelimiterDetector` for automatic delimiter detection and `Csv.Validate()` for structural CSV validation.
- IDataReader Support (Jan 2026): Added `Csv.CreateDataReader()` and `FixedWidth.CreateDataReader()` for streaming database bulk-load scenarios via `DbDataReader`.
- Fixed-Width Enhancements (Jan 2026): Added fixed-length row skipping/validation, `AllowMissingColumns`, comment line skipping, and field layout validation.
- Performance Fixes (Jan-Mar 2026): Eliminated `ToString()` allocations in hot paths (delimiter detection, integer discriminator mapping). Fixed nullable Row/Column in `CsvException`.

<!-- MANUAL ADDITIONS START -->

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
Both CSV and Fixed-Width support `DbDataReader` for streaming large files into databases.
- `Csv.CreateDataReader(stream|path)` - CSV streaming reader
- `FixedWidth.CreateDataReader(stream|path)` - Fixed-width streaming reader
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
- Always include a justification comment explaining WHY the suppression is acceptable

### Avoid ToString() Allocations in Hot Paths
- Use `ReadOnlySpan<char>` or `ReadOnlySpan<byte>` comparisons instead of converting to strings
- For integer discriminator keys, compare numeric values directly rather than calling `ToString()`
- Prefer `stackalloc` or `ArrayPool` over heap allocations in tight loops

## Performance Optimization Lessons

### What Works

- **CLMUL-based quote handling**: The PCLMULQDQ instruction for branchless prefix XOR provides efficient quote-aware SIMD parsing
- **Compile-time specialization**: Generic type parameters (`TQuotePolicy`, `TTrack`) allow JIT to eliminate dead code paths
- **`AppendColumn` method**: The JIT already optimizes this well - don't try to "improve" it
- **ArrayPool for buffer reuse**: `CsvCharToByteBinderAdapter` uses `ArrayPool<byte>.Shared` for char-to-byte conversion buffers, reducing GC pressure
- **Stackalloc for small arrays**: Column byte lengths use stackalloc when ≤128 columns, avoiding heap allocations entirely

### What Doesn't Work

Attempted optimizations that caused regressions:

1. **Batch validation with PopCount**: Using `BitOperations.PopCount()` to count delimiters and validate bounds once per chunk actually adds overhead. The per-delimiter check in `AppendColumn` is already well-optimized by the JIT.

2. **Unsafe.Add for columnEnds writes**: Replacing array indexing with `Unsafe.Add(ref columnEndsRef, index)` didn't help - the JIT already eliminates bounds checks when it can prove safety.

3. **Hoisting maxFieldLength checks**: Pre-computing `maxFieldLength ?? int.MaxValue` and using a `checkLimit` boolean adds more overhead than the nullable check itself.

**Key insight**: The .NET JIT is very good at optimizing simple, idiomatic code. "Clever" micro-optimizations often backfire by preventing JIT optimizations or adding instruction overhead.

### Benchmark Baseline (vs Sep 0.12.1)

**Latest Results (.NET 10, AVX-512, AMD Ryzen AI 9 HX PRO 370):**

HeroParser UTF-8 is now **faster than Sep** in all tested scenarios, including quoted data.

**Performance Summary (Jan 2026)**:
- **Standard (10k rows x 25 cols)**: HeroParser UTF-8 takes **0.79x the time** of Sep (quoted) and **0.93x** (unquoted) — ~21% and ~7% faster respectively.
- **Wide CSVs**: **25-45% faster** than Sep.
- **Allocations**: **4 KB fixed** (vs Sep's variable 2-13KB).
- **Recommendation**: Always use UTF-8 APIs (`byte[]`). UTF-16 is deprecated for performance.

**Historical Note**: UTF-16 Pack-Saturate approach was abandoned due to memory traffic overhead.
**Unicode**: Verified correct handling for Chinese, Arabic, Emoji, and Mixed Unicode.

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
- **Row Scanner**: Uses SIMD to find delimiters + newlines in parallel. PCLMULQDQ for branchless quote tracking.
- **Column Extraction**: `AppendColumn` tracks column boundaries via `columnEnds[]` array.
- **Binding**: `ICsvBinder<TElement, T>` maps columns to record properties. Source-generated binders inline type parsing.
- **UTF-16 fallback**: `CsvCharToByteBinderAdapter` converts to UTF-8 via `ArrayPool` + `stackalloc`, then uses the byte path.

### Write Path
```
Records → PropertyAccessor (compiled expression trees) → CsvStreamWriter (buffered) → TextWriter
                                                              │
                                                         Quote analysis (SIMD AVX2/SSE2)
```

### Key Abstractions
- `CsvRowReader<T>` — ref struct row iterator (T = byte or char)
- `CsvRecordReader<TElement, T>` — ref struct that wraps row reader + binder
- `CsvStreamWriter` — buffered writer with `ArrayPool<char>` management
- `CsvAsyncStreamWriter` — async variant with `char[]` + `byte[]` dual buffers
- `ICsvBinder<TElement, T>` — interface for source-generated and reflection binders

## Troubleshooting

### Lock file out of date
```
error NU1004: The packages lock file is inconsistent with the project dependencies
```
**Fix**: Run `dotnet restore --force-evaluate` to regenerate lock files, then commit the updated `packages.lock.json`.

### AOT trim warnings
If `dotnet publish -r linux-x64 --self-contained` produces trim warnings:
- Ensure record types use `[CsvGenerateBinder]` or `[FixedWidthGenerateBinder]` attributes
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

<!-- MANUAL ADDITIONS END -->
