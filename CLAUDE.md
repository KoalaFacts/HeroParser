# HeroParser Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-01-09

## Active Technologies
- C# with multi-framework targeting (net8.0, net9.0, net10.0) + BenchmarkDotNet (performance validation), Source Generators (allocation-free mapping), Zero external dependencies for core library (001-aim-to-be)

## Project Structure
```
src/
tests/
```

## Commands
# Add commands for C# with multi-framework targeting (net8.0, net9.0, net10.0)

## Code Style
C# with multi-framework targeting (net8.0, net9.0, net10.0): Follow standard conventions

## Recent Changes
- 001-aim-to-be: Added C# with multi-framework targeting (net8.0, net9.0, net10.0) + BenchmarkDotNet (performance validation), Source Generators (allocation-free mapping), Zero external dependencies for core library
- Multi-Schema CSV: Added multi-schema CSV parsing for banking/financial file formats with header/detail/trailer patterns
- UTF-8 Consolidation (Jan 2026): Unified SIMD parsing to UTF-8 only path. UTF-16 string API now converts to UTF-8 internally via optimized `CsvCharToByteBinderAdapter` using ArrayPool and stackalloc. Result: HeroParser UTF-8 now matches or beats Sep 0.12.1 in most benchmarks.

<!-- MANUAL ADDITIONS START -->

## Multi-Schema CSV Parsing
Supports mapping rows to different record types via a discriminator column.
- **Runtime**: Flexible, uses `WithMultiSchema().WithDiscriminator("Type")`.
- **Source-Generated**: Optimal performance (`[CsvGenerateDispatcher]`). 2x faster than runtime.
Key files in `src/HeroParser/SeparatedValues/Records/MultiSchema/`.

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