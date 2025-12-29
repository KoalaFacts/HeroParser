# HeroParser Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-12-27

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
[CsvMultiSchemaDispatcher(DiscriminatorIndex = 0)]
public partial class BankingDispatcher
{
    [CsvDiscriminator("H")]
    public static partial HeaderRecord? BindHeader(CsvRow<char> row, int rowNumber);

    [CsvDiscriminator("D")]
    public static partial DetailRecord? BindDetail(CsvRow<char> row, int rowNumber);

    [CsvDiscriminator("T")]
    public static partial TrailerRecord? BindTrailer(CsvRow<char> row, int rowNumber);
}

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

### What Doesn't Work

Attempted optimizations that caused regressions:

1. **Batch validation with PopCount**: Using `BitOperations.PopCount()` to count delimiters and validate bounds once per chunk actually adds overhead. The per-delimiter check in `AppendColumn` is already well-optimized by the JIT.

2. **Unsafe.Add for columnEnds writes**: Replacing array indexing with `Unsafe.Add(ref columnEndsRef, index)` didn't help - the JIT already eliminates bounds checks when it can prove safety.

3. **Hoisting maxFieldLength checks**: Pre-computing `maxFieldLength ?? int.MaxValue` and using a `checkLimit` boolean adds more overhead than the nullable check itself.

**Key insight**: The .NET JIT is very good at optimizing simple, idiomatic code. "Clever" micro-optimizations often backfire by preventing JIT optimizations or adding instruction overhead.

### Benchmark Baseline (vs Sep)

**Latest Results (.NET 9, AVX-512, 10k rows × 25 cols):**

| Encoding | Scenario | HeroParser | Sep | Ratio | Winner |
|----------|----------|------------|-----|-------|--------|
| UTF-16 | Unquoted | 440.7 μs | 336.4 μs | 1.31x slower | Sep |
| UTF-16 | Quoted | 857.5 μs | 656.8 μs | 1.31x slower | Sep |
| UTF-8 | Unquoted | 364.1 μs | 336.4 μs | 1.08x slower | Sep |
| UTF-8 | Quoted | 693.2 μs | 656.8 μs | 1.06x slower | Sep |

**Note:** Performance varies based on data characteristics. The CLMUL-based quote handling provides efficiency gains for complex quoted data patterns, though Sep's optimized architecture maintains an edge in general benchmarks.

**UTF-16 Pack-Saturate Research (Dec 2024):**

Attempted full implementation of Sep's pack-saturate approach to match their UTF-16 performance:

**Implementation:**
- Changed from 32 chars/iteration to 64 chars/iteration
- Used `PackUnsignedSaturate` to pack two `Vector512<ushort>` (64 chars) into one `Vector512<byte>`
- Used `PermuteVar8x64` to unshuffle interleaved bytes from pack operation
- Created 64-bit CLMUL function to process 64-bit masks (vs baseline 32-bit)
- All tests passed (492 tests across .NET 8.0, 9.0, 10.0)

**Benchmark Results:**
| Scenario | Baseline | Pack-Saturate | Change |
|----------|----------|---------------|--------|
| UTF-16 Unquoted | 401.7 μs | 404.1 μs | **3-6% slower** ❌ |
| UTF-16 Quoted | 782.0 μs | 737.3 μs | ~6% faster (but still slower than expected) |
| UTF-8 Unquoted | 340.9 μs | Regressed | Performance degraded |

**Root Cause of Regression:**
1. **Double memory traffic**: Loading two `Vector512<ushort>` reads 128 bytes vs baseline's 64 bytes
2. **Instruction overhead**: `PackUnsignedSaturate` + `PermuteVar8x64` adds latency
3. **64-bit CLMUL complexity**: Processing 64-bit masks requires splitting into two 32-bit chunks, managing intermediate state
4. **Longer dependency chains**: More operations before CLMUL can begin

**Why Sep is Faster:**
- Sep's architecture is designed from scratch around pack-saturate (likely without CLMUL)
- HeroParser's CLMUL-based quote handling is optimized for 32-bit masks
- Different design philosophies, both valid for their respective architectures

**Decision:**
- **Reverted** pack-saturate implementation back to 32 chars/iteration baseline
- UTF-16's 1.23x gap vs Sep is acceptable given fundamental 2x memory bandwidth overhead
- Micro-benchmark gains (delimiter detection only) don't translate to full parsing workload

**Recommendation**: Focus on UTF-8 performance where HeroParser is closest to Sep (within 6-8%) rather than UTF-16 optimization.

### Unicode Handling

**Tested character sets** (Dec 2024):
- ✅ Chinese: Both Sep and HeroParser handle correctly
- ✅ Arabic: Both parsers handle correctly
- ✅ Emoji: Both parsers handle correctly
- ✅ Mixed Unicode: Full support in both parsers

**Initial hypothesis about Sep's char→byte saturation causing Unicode issues was incorrect.** Both parsers handle full Unicode properly.

<!-- MANUAL ADDITIONS END -->