# HeroParser v3.0 - Final Implementation Specification

**Date:** 2025-11-15
**Status:** ✅ APPROVED - Ready to Implement

---

## Executive Summary

Complete rewrite of HeroParser focusing on:
- ✅ Clean, simple API (2 methods)
- ✅ No unsafe code
- ✅ Multi-framework support (netstandard2.0 → net10.0)
- ✅ Proper error handling with error codes
- ✅ 30+ GB/s performance (net6+)

---

## API Surface (Final)

### Public API - 2 Methods

```csharp
public static class Csv
{
    public static CsvReader Parse(string csv, CsvParserOptions? options = null);

    public static IAsyncEnumerable<CsvRow> ParseAsync(
        string csv,
        CsvParserOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

### Options Class

```csharp
public sealed class CsvParserOptions
{
    public char Delimiter { get; init; } = ',';
    public int MaxColumns { get; init; } = 10_000;
    public int MaxRows { get; init; } = 100_000;
    public static CsvParserOptions Default { get; } = new();
}
```

### Exception with Error Codes

```csharp
public enum CsvErrorCode
{
    TooManyColumns = 1,
    TooManyRows = 2,
    InvalidDelimiter = 3,
    InvalidOptions = 4,
    ParseError = 99
}

public class CsvException : Exception
{
    public CsvErrorCode ErrorCode { get; }
    public int? Row { get; }
    public int? Column { get; }
}
```

---

## Target Frameworks

```xml
<TargetFrameworks>
  netstandard2.0;netstandard2.1;
  net6.0;net7.0;net8.0;net9.0;net10.0
</TargetFrameworks>
```

**Performance Expectations:**
- netstandard2.0: 2-5 GB/s (scalar only)
- netstandard2.1: 5-10 GB/s (limited SIMD)
- net6.0+: 30+ GB/s (full AVX-512/AVX2/NEON)

---

## Core Principles (Preserved)

1. ⚡ **Ultra-high performance** - 30+ GB/s with SIMD
2. 🚫 **Zero allocations** - Hot path uses ref structs, ArrayPool
3. 📦 **Zero dependencies** - Core library standalone
4. 🎯 **Simple API** - Just 2 methods
5. ✅ **No unsafe code** - Use safe MemoryMarshal APIs

---

## File Structure

```
src/HeroParser/
├── Csv.cs                      # Public API
├── CsvParserOptions.cs         # Options
├── CsvException.cs             # Exception + error codes
├── CsvReader.cs                # Sync reader (ref struct)
├── CsvRow.cs                   # Row accessor (ref struct)
├── CsvCol.cs                   # Column value (ref struct)
├── Simd/
│   ├── ISimdParser.cs          # Parser interface
│   ├── ScalarParser.cs         # Baseline (all frameworks)
│   ├── Avx512Parser.cs         # net6+ only
│   ├── Avx2Parser.cs           # net6+ only
│   ├── NeonParser.cs           # net6+ only
│   └── SimdParserFactory.cs    # Hardware detection
└── HeroParser.csproj
```

**Total: ~12 files**

---

## Implementation Phases

### ✅ Phase 1: Core (6 hours)
- [x] Planning complete
- [ ] CsvParserOptions
- [ ] CsvException with error codes
- [ ] CsvReader, CsvRow, CsvCol (ref structs)
- [ ] ScalarParser (works on all frameworks)
- [ ] Csv.Parse() implementation
- [ ] Basic tests

**Deliverable:** Working parser on netstandard2.0

### Phase 2: SIMD (8 hours)
- [ ] Avx512Parser (safe, net6+)
- [ ] Avx2Parser (safe, net6+)
- [ ] NeonParser (safe, net6+)
- [ ] SimdParserFactory with conditional compilation
- [ ] SIMD correctness tests

**Deliverable:** 30+ GB/s on net6+

### Phase 3: Async (4 hours)
- [ ] Csv.ParseAsync() implementation
- [ ] IAsyncEnumerable polyfill for netstandard2.0
- [ ] Chunked processing
- [ ] Cancellation support
- [ ] Async tests

**Deliverable:** Complete async API

### Phase 4: Polish (4 hours)
- [ ] Multi-framework build validation
- [ ] Benchmarks on all frameworks
- [ ] XML documentation
- [ ] README updates
- [ ] NuGet package

**Deliverable:** Production v3.0

---

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Unsafe code | ❌ No | Safe alternatives exist, minimal overhead |
| Input type | `string` | Simple for now, add Span later |
| Max columns | 10,000 | Reasonable limit |
| Max rows | 100,000 | Reasonable limit |
| Exceed limits | Throw exception | Fail fast, clear error |
| Error handling | Error codes | Easy to catch specific errors |
| File I/O | Not included | User responsibility |
| Parallel | Not included | SIMD is fast enough |
| Quotes | Not supported | Speed over features |

---

## Success Criteria

- [ ] Builds on all 7 frameworks
- [ ] Zero unsafe code
- [ ] All tests pass (all frameworks)
- [ ] 30+ GB/s on net6+ (benchmarked)
- [ ] Zero allocations on net6+ (verified)
- [ ] Full XML documentation
- [ ] NuGet package ready

---

## Next: Start Implementation

**Current status:** All planning complete ✅

**Ready to begin:** Phase 1 - Core functionality

**First file to create:** `src/HeroParser/HeroParser.csproj`

---

## Documents Created

1. ✅ CODE_AUDIT_REPORT.md - 23 issues identified
2. ✅ UNSAFE_CODE_ANALYSIS.md - Removal feasibility
3. ✅ REWRITE_PLAN.md - Initial rewrite plan
4. ✅ V3_SIMPLIFIED_API.md - API design
5. ✅ V3_EXCEPTION_DESIGN.md - Exception with codes
6. ✅ V3_FINAL_SPEC.md (this document) - Final spec

**All committed to:** `claude/code-audit-01W2VnoFLyphK9S8ensCLSLE`

---

🚀 **Ready to start building!**
