# HeroParser Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-11-29

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

- **CLMUL-based quote handling**: The PCLMULQDQ instruction for branchless prefix XOR enables ~6% faster quoted CSV parsing than Sep
- **Compile-time specialization**: Generic type parameters (`TQuotePolicy`, `TTrack`) allow JIT to eliminate dead code paths
- **`AppendColumn` method**: The JIT already optimizes this well - don't try to "improve" it

### What Doesn't Work

Attempted optimizations that caused regressions:

1. **Batch validation with PopCount**: Using `BitOperations.PopCount()` to count delimiters and validate bounds once per chunk actually adds overhead. The per-delimiter check in `AppendColumn` is already well-optimized by the JIT.

2. **Unsafe.Add for columnEnds writes**: Replacing array indexing with `Unsafe.Add(ref columnEndsRef, index)` didn't help - the JIT already eliminates bounds checks when it can prove safety.

3. **Hoisting maxFieldLength checks**: Pre-computing `maxFieldLength ?? int.MaxValue` and using a `checkLimit` boolean adds more overhead than the nullable check itself.

**Key insight**: The .NET JIT is very good at optimizing simple, idiomatic code. "Clever" micro-optimizations often backfire by preventing JIT optimizations or adding instruction overhead.

### Benchmark Baseline (vs Sep)

**Latest Results (.NET 10, AVX-512, 10k rows × 25 cols):**

| Encoding | Scenario | HeroParser | Sep | Ratio | Winner |
|----------|----------|------------|-----|-------|--------|
| UTF-16 | Unquoted | 404.6 μs | 333.9 μs | 1.21x slower | Sep |
| UTF-16 | Quoted | 732.9 μs | 630.9 μs | 1.16x slower | Sep |
| UTF-8 | Unquoted | 345.6 μs | 333.9 μs | 1.03x slower | Sep |
| UTF-8 | Quoted | 594.9 μs | 630.9 μs | **0.94x (6% faster!)** | **HeroParser** ✅ |

**UTF-16 Pack-Saturate Research (Dec 2024):**

Investigated Sep's approach of packing UTF-16 chars to bytes using `PackUnsignedSaturate`:
- **Theory**: Pack 64 chars → bytes, compare as bytes (2x throughput vs native ushort compare)
- **Micro-benchmark results**: 20-30% faster for simple delimiter detection
- **Challenge**: `PackUnsignedSaturate` shuffles bytes; requires complex position mapping for actual parsing
- **Decision**: Not implemented due to complexity vs marginal benefit
  - UTF-16 has inherent 2x memory bandwidth overhead (fundamental limit)
  - UTF-8 is already competitive and preferred for modern workloads
  - Implementation complexity high; testing burden significant
  - 20% improvement on UTF-16 would still be ~1.00x vs Sep (barely tied)

**Recommendation**: Focus on UTF-8 performance (already excellent) rather than UTF-16 optimization.

### Unicode Handling

**Tested character sets** (Dec 2024):
- ✅ Chinese: Both Sep and HeroParser handle correctly
- ✅ Arabic: Both parsers handle correctly
- ✅ Emoji: Both parsers handle correctly
- ✅ Mixed Unicode: Full support in both parsers

**Initial hypothesis about Sep's char→byte saturation causing Unicode issues was incorrect.** Both parsers handle full Unicode properly.

<!-- MANUAL ADDITIONS END -->