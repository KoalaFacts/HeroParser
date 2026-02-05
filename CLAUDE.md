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

HeroParser supports multi-schema CSV parsing where different rows map to different record types based on a discriminator column. This is common in banking/financial formats (NACHA, BAI, EDI).

### API Overview

```csharp
// Fluent builder API
foreach (var record in Csv.Read()
    .WithMultiSchema()
    .WithDiscriminator("Type")      // By column name or index
    .MapRecord<HeaderRecord>("H")
    .MapRecord<DetailRecord>("D")
    .MapRecord<TrailerRecord>("T")
    .AllowMissingColumns()
    .FromText(csv))
{
    // Use pattern matching to handle different record types
    switch (record)
    {
        case HeaderRecord h: // ...
        case DetailRecord d: // ...
        case TrailerRecord t: // ...
    }
}
```

### Key Files

- `src/HeroParser/SeparatedValues/Records/MultiSchema/CsvMultiSchemaReaderBuilder.cs` - Fluent builder
- `src/HeroParser/SeparatedValues/Records/MultiSchema/CsvMultiSchemaBinder.cs` - Type dispatching
- `src/HeroParser/SeparatedValues/Records/MultiSchema/CsvMultiSchemaRecordReader.cs` - Span-based reader
- `src/HeroParser/SeparatedValues/Records/MultiSchema/CsvMultiSchemaStreamingRecordReader.cs` - Stream-based reader
- `src/HeroParser/SeparatedValues/Records/MultiSchema/UnmatchedRowBehavior.cs` - Enum for unmatched row handling

### Features

- Discriminator by column index or header name
- Case-sensitive or case-insensitive discriminator matching
- Skip, throw, or use custom factory for unmatched rows
- Streaming from files, streams, and async variants
- Works with source-generated binders (`[CsvGenerateBinder]`)

### Source-Generated Multi-Schema Dispatch (Optimal Performance)

For maximum performance, use source-generated dispatchers instead of runtime multi-schema:

```csharp
[CsvGenerateDispatcher(DiscriminatorIndex = 0)]
[CsvSchemaMapping("H", typeof(HeaderRecord))]
[CsvSchemaMapping("D", typeof(DetailRecord))]
[CsvSchemaMapping("T", typeof(TrailerRecord))]
public partial class BankingDispatcher { }

// Usage:
var reader = Csv.Read().FromText(csv);
if (reader.MoveNext()) { } // Skip header
while (reader.MoveNext())
{
    var record = BankingDispatcher.Dispatch(reader.Current, rowNumber);
    // Handle record...
}
```

**Multi-Schema Performance Results (.NET 9, 1000 rows):**

| Method | Mean | vs Baseline |
|--------|------|-------------|
| SingleSchema_TypedBinder (baseline) | 101.6 μs | 1.00x |
| MultiSchema_Runtime (dictionary lookup) | 150.7 μs | 1.48x slower |
| **MultiSchema_SourceGenerated** | **52.8 μs** | **1.92x faster** |

**Why source-generated is faster:**
- Switch expression compiles to jump table (no dictionary lookup)
- Direct binder invocation (no interface dispatch)
- No boxing/unboxing overhead
- 2.85x faster than runtime multi-schema dispatch

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

**Standard Workload (10k rows × 25 cols):**

| Encoding | Scenario | HeroParser | Sep | Ratio | Winner |
|----------|----------|------------|-----|-------|--------|
| UTF-8 | Unquoted | 551.6 μs | 608.5 μs | **0.93x faster** | **HeroParser** |
| UTF-8 | Quoted | 1,705 μs | 2,165 μs | **0.79x faster** | **HeroParser** |
| UTF-16 | Unquoted | 2,498 μs | 608.5 μs | 4.1x slower | Sep |
| UTF-16 | Quoted | 3,437 μs | 1,204 μs | 2.9x slower | Sep |

**Wide CSV Performance (where HeroParser excels):**

| Rows | Cols | Quotes | Sep | HeroParser UTF-8 | Ratio |
|------|------|--------|-----|------------------|-------|
| 100 | 50 | No | 10.4 μs | 8.4 μs | **0.81x** |
| 100 | 100 | No | 23.7 μs | 15.3 μs | **0.65x** |
| 100 | 100 | Yes | 48.1 μs | 35.2 μs | **0.73x** |
| 1,000 | 50 | Yes | 226 μs | 174 μs | **0.78x** |
| 1,000 | 100 | Yes | 429 μs | 281 μs | **0.70x** |
| 10,000 | 50 | No | 1,437 μs | 778 μs | **0.55x** |
| 10,000 | 100 | Yes | 6,363 μs | 3,617 μs | **0.60x** |
| 100,000 | 100 | No | 21,836 μs | 14,568 μs | **0.67x** |
| 100,000 | 100 | Yes | 46,580 μs | 35,396 μs | **0.76x** |

**Memory Allocation:**
- Sep: 1.98 - 13.09 KB (varies with column count)
- HeroParser: **4 KB fixed** (regardless of column count)

**Key Findings (Jan 2026):**
- **Wide CSVs (50-100 columns)**: HeroParser UTF-8 is **25-45% faster** than Sep
- **Quoted UTF-8**: HeroParser UTF-8 is **21% faster** than Sep (Jan 2026 re-verification)
- **Narrow CSVs (10-25 columns)**: Performance is comparable (within ±15%)
- **Quoted data with many columns**: HeroParser is significantly faster
- UTF-16 path is now deprecated for performance-critical scenarios

**Recommendation**: Always use UTF-8 APIs (`byte[]` or `ReadOnlySpan<byte>`) for best performance. The UTF-16 (`string`) path exists for convenience but incurs significant overhead.

**Historical Note - UTF-16 Pack-Saturate Research (Dec 2024):**

Attempted full implementation of Sep's pack-saturate approach for UTF-16. Results showed 3-6% regression due to:
- Double memory traffic (128 bytes vs 64 bytes per iteration)
- Instruction overhead from `PackUnsignedSaturate` + `PermuteVar8x64`
- 64-bit CLMUL complexity vs optimized 32-bit masks

Decision: Reverted and focused on UTF-8 optimization instead, which proved successful.

### Unicode Handling

**Tested character sets** (Dec 2024):
- ✅ Chinese: Both Sep and HeroParser handle correctly
- ✅ Arabic: Both parsers handle correctly
- ✅ Emoji: Both parsers handle correctly
- ✅ Mixed Unicode: Full support in both parsers

**Initial hypothesis about Sep's char→byte saturation causing Unicode issues was incorrect.** Both parsers handle full Unicode properly.

<!-- MANUAL ADDITIONS END -->