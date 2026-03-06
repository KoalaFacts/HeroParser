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

# Check code formatting
dotnet format --verify-no-changes

# Run benchmarks
dotnet run -c Release --project <benchmark-project>
```

## Code Style
- **Nullable reference types**: Enabled (`<Nullable>enable</Nullable>`)
- **Language version**: Latest (`<LangVersion>latest</LangVersion>`)
- **Implicit usings**: Enabled
- **File-scoped namespaces**: Preferred (warning severity)
- **Private fields**: `camelCase` (no `s_` prefix for static fields, no `_` prefix)
- **Constants**: `UPPER_CASE_WITH_UNDERSCORES`
- **Interfaces**: `IInterfaceName` (prefix `I`, PascalCase)
- **Types, Properties, Methods**: `PascalCase`
- **`var` usage**: At discretion (IDE0008 disabled)
- **Braces**: Optional for single-line blocks (IDE0011 disabled)
- **Build enforces code style**: `EnforceCodeStyleInBuild = true`, `TreatWarningsAsErrors = true`
- **Collection initialization**: Use modern syntax (IDE0300-0305 enforced as warnings)
- **Formatting**: Must pass `dotnet format --verify-no-changes` in CI

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
- **Standard (10k rows x 25 cols)**: HeroParser UTF-8 is **0.79x faster** (quoted) and **0.93x faster** (unquoted) than Sep.
- **Wide CSVs**: **25-45% faster** than Sep.
- **Allocations**: **4 KB fixed** (vs Sep's variable 2-13KB).
- **Recommendation**: Always use UTF-8 APIs (`byte[]`). UTF-16 is deprecated for performance.

**Historical Note**: UTF-16 Pack-Saturate approach was abandoned due to memory traffic overhead.
**Unicode**: Verified correct handling for Chinese, Arabic, Emoji, and Mixed Unicode.

<!-- MANUAL ADDITIONS END -->
