# HeroParser v3.0 - Clean Rewrite Plan

**Date:** 2025-11-15
**Approach:** Start from scratch with lessons learned
**Philosophy:** Keep the speed, lose the baggage

---

## Core Principles (PRESERVED)

These are **non-negotiable** - the soul of HeroParser:

### 1. ⚡ Ultra-High Performance
- **Target:** 30+ GB/s on AVX-512 hardware
- **Method:** SIMD optimization (AVX-512, AVX2, ARM NEON)
- **Proof:** Must beat Sep library in benchmarks

### 2. 🚫 Zero Allocations
- **Hot path:** No heap allocations during parsing
- **Method:** Ref structs, spans, ArrayPool
- **Validation:** Memory profiler confirms zero GC pressure

### 3. 📦 Zero Dependencies
- **Core library:** No external packages
- **Exception:** BenchmarkDotNet for testing only
- **Benefit:** Minimal supply chain risk

### 4. 🎯 Simple API
- **Philosophy:** 5 methods or fewer
- **Design:** Easy things easy, complex things possible
- **Example:** `foreach (var row in Csv.Parse(data)) { ... }`

---

## What We're Removing

### ❌ Everything Gets Deleted
- All current source code
- All current tests
- Broken implementations (CsvFileReader)
- Unsafe code patterns
- Confusing abstractions

### ✅ What We Keep
- README.md (update with v3.0 info)
- CLAUDE.md (update with new stack)
- Benchmark infrastructure concept
- Git history (for reference)
- Learning from audit findings

---

## New Architecture Design

### Clean Layered Design

```
┌─────────────────────────────────────┐
│   Public API (Csv.cs)               │  ← 1 file, 5 methods max
├─────────────────────────────────────┤
│   Core Reader (CsvReader.cs)        │  ← ref struct, enumerable
├─────────────────────────────────────┤
│   Row/Column (CsvRow.cs, CsvCol.cs) │  ← ref structs, zero-alloc
├─────────────────────────────────────┤
│   SIMD Strategy (IParser interface) │  ← clean abstraction
├─────────────────────────────────────┤
│   SIMD Implementations              │  ← AVX512, AVX2, NEON, Scalar
│   ├─ Avx512Parser.cs                │  ← NO UNSAFE
│   ├─ Avx2Parser.cs                  │  ← NO UNSAFE
│   ├─ NeonParser.cs                  │  ← NO UNSAFE
│   └─ ScalarParser.cs                │  ← Pure C# baseline
└─────────────────────────────────────┘
```

**Total files:** ~8 (down from ~15)

---

## Key Improvements Over v2.0

### 1. No Unsafe Code ✅
```csharp
// OLD (v2.0) - unsafe
public unsafe int ParseColumns(...)
{
    fixed (char* ptr = line) { ... }
}

// NEW (v3.0) - safe
public int ParseColumns(...)
{
    ref readonly char start = ref MemoryMarshal.GetReference(line);
    // Use Unsafe.Add, Vector.LoadUnsafe - NO unsafe keyword
}
```

### 2. Proper Error Handling ✅
```csharp
// OLD - silent failures
if (columnCount < buffer.Length) { ... }  // Silently truncates!

// NEW - explicit errors
if (columnCount >= buffer.Length)
    throw new CsvException($"Row has more than {MaxColumns} columns");
```

### 3. Correct Resource Management ✅
```csharp
// OLD - leaks on exception
var array = ArrayPool<int>.Shared.Rent(size);
Parse(...);  // May throw!
return new CsvRow(array);  // Leaked!

// NEW - exception-safe
var array = ArrayPool<int>.Shared.Rent(size);
try
{
    Parse(...);
    return new CsvRow(array);
}
catch
{
    ArrayPool<int>.Shared.Return(array, clearArray: true);
    throw;
}
```

### 4. Proper Bounds Checking ✅
```csharp
// OLD - removed for speed
public CsvCol this[int index] => _cols[index];  // May crash!

// NEW - debug assertions
public CsvCol this[int index]
{
    get
    {
        #if DEBUG
        if ((uint)index >= (uint)Count)
            throw new IndexOutOfRangeException();
        #endif
        return _cols[index];
    }
}
```

### 5. Single Framework Target ✅
```csharp
// CLAUDE.md says: multi-framework (wrong!)
// .csproj says: net8.0 (correct!)

// NEW: Pick ONE
<TargetFramework>net8.0</TargetFramework>  // Latest features
// OR
<TargetFrameworks>net6.0;net8.0</TargetFrameworks>  // Wide compat
```

### 6. No File I/O (Simplification) ✅
```csharp
// OLD - Broken CsvFileReader (defeats memory-mapping)
Csv.ParseFile("data.csv")

// NEW - Let users handle files
var data = File.ReadAllText("data.csv");
var reader = Csv.Parse(data);

// Future: Proper streaming API if needed
```

---

## Simplified API

### The Entire Public API (3 methods)

```csharp
public static class Csv
{
    /// <summary>
    /// Parse CSV data with specified delimiter.
    /// </summary>
    public static CsvReader Parse(ReadOnlySpan<char> csv, char delimiter = ',');

    /// <summary>
    /// Parse standard comma-delimited CSV (optimized).
    /// </summary>
    public static CsvReader ParseComma(ReadOnlySpan<char> csv);

    /// <summary>
    /// Parse tab-delimited CSV (optimized).
    /// </summary>
    public static CsvReader ParseTab(ReadOnlySpan<char> csv);
}
```

**That's it.** No parallel parsing (premature optimization), no file reading (let users decide).

---

## Implementation Plan

### Phase 1: Foundation (Day 1) 🏗️

**Goal:** Working scalar implementation with tests

1. **Create basic structure**
   - `src/HeroParser/Csv.cs` - API entry point
   - `src/HeroParser/CsvReader.cs` - Core reader
   - `src/HeroParser/CsvRow.cs` - Row accessor
   - `src/HeroParser/CsvCol.cs` - Column value

2. **Implement ScalarParser**
   - Pure C# implementation
   - No SIMD yet
   - Correctness baseline
   - ~100 lines of code

3. **Write comprehensive tests**
   - Basic parsing
   - Edge cases (empty, single column, etc.)
   - Line endings (LF, CRLF, CR)
   - Error conditions

**Deliverable:** Slow but correct CSV parser with 100% test coverage

---

### Phase 2: SIMD Optimization (Day 2-3) 🚀

**Goal:** Fast, safe SIMD implementations

1. **Create SIMD abstraction**
   ```csharp
   internal interface ISimdParser
   {
       int ParseColumns(ReadOnlySpan<char> line, char delimiter,
                        Span<int> starts, Span<int> lengths);
   }
   ```

2. **Implement Avx512Parser (safe)**
   - Use `MemoryMarshal.GetReference`
   - Use `Vector512.LoadUnsafe`
   - No unsafe keyword
   - Target: 30+ GB/s

3. **Implement Avx2Parser (safe)**
   - Use `Vector256.LoadUnsafe`
   - Fallback for older CPUs
   - Target: 20+ GB/s

4. **Implement NeonParser (safe + optimized)**
   - Fix ExtractMask performance bug
   - Use proper ARM intrinsics
   - Target: 12+ GB/s

5. **Hardware detection**
   - Select best parser at startup
   - Provide diagnostics API

**Deliverable:** World-class SIMD performance, all safe code

---

### Phase 3: Polish & Validation (Day 4) ✨

**Goal:** Production-ready quality

1. **Resource management audit**
   - Verify ArrayPool cleanup
   - Test exception paths
   - Memory leak detection

2. **Performance validation**
   - BenchmarkDotNet suite
   - Compare vs Sep library
   - Verify zero allocations

3. **Documentation**
   - XML comments on all public APIs
   - README with examples
   - Performance claims with proof

4. **Configuration**
   - Clean .csproj
   - No duplicate settings
   - Proper package metadata

**Deliverable:** v3.0 ready for release

---

## File Structure

```
HeroParser/
├── src/
│   └── HeroParser/
│       ├── Csv.cs                      # Public API (3 methods)
│       ├── CsvReader.cs                # Core reader (ref struct)
│       ├── CsvRow.cs                   # Row accessor (ref struct)
│       ├── CsvCol.cs                   # Column value (ref struct)
│       ├── Simd/
│       │   ├── ISimdParser.cs          # Parser interface
│       │   ├── ScalarParser.cs         # Baseline (no SIMD)
│       │   ├── Avx512Parser.cs         # AVX-512 (safe)
│       │   ├── Avx2Parser.cs           # AVX2 (safe)
│       │   ├── NeonParser.cs           # ARM NEON (safe)
│       │   └── SimdParserFactory.cs    # Hardware detection
│       └── HeroParser.csproj
├── tests/
│   └── HeroParser.Tests/
│       ├── BasicTests.cs               # Core functionality
│       ├── EdgeCaseTests.cs            # Boundaries
│       ├── SimdTests.cs                # SIMD correctness
│       ├── PerformanceTests.cs         # Allocation/speed
│       └── HeroParser.Tests.csproj
├── benchmarks/
│   └── HeroParser.Benchmarks/
│       ├── VsSepBenchmark.cs           # Head-to-head
│       └── HeroParser.Benchmarks.csproj
├── README.md
├── CLAUDE.md                            # Updated principles
├── LICENSE
└── HeroParser.sln
```

**Total source files:** ~10 (clean!)

---

## Technical Decisions

### ✅ Decisions Made

| Question | Decision | Rationale |
|----------|----------|-----------|
| Unsafe code? | **NO** | Safe alternatives exist, <2% overhead |
| Multi-framework? | **net8.0 only** | Latest features, simpler to maintain |
| File I/O? | **NO** | Let users handle, avoid complexity |
| Parallel parsing? | **NO** | YAGNI, adds complexity |
| Quote handling? | **NO** | Speed over features (documented) |
| Max columns? | **10,000** | Reasonable limit, error if exceeded |
| Error handling? | **Exceptions** | Fail fast, clear errors |
| ArrayPool clearing? | **YES** | Security best practice |

### 🎯 Performance Targets

| Metric | Target | Validation |
|--------|--------|------------|
| AVX-512 | 30+ GB/s | BenchmarkDotNet |
| AVX2 | 20+ GB/s | BenchmarkDotNet |
| ARM NEON | 12+ GB/s | BenchmarkDotNet |
| Allocations | 0 (hot path) | Memory Profiler |
| vs Sep | 1.5x faster | Head-to-head |

---

## Migration for Existing Users

### v2.0 → v3.0 Changes

```csharp
// REMOVED APIs
❌ Csv.ParseFile(path)           // Use File.ReadAllText
❌ Csv.ParseParallel(csv)        // Use Csv.Parse (fast enough)
❌ CsvFileReader                 // Use File.ReadAllText + Csv.Parse

// KEPT APIs (unchanged)
✅ Csv.Parse(csv, delimiter)
✅ Csv.ParseComma(csv)
✅ Csv.ParseTab(csv)
✅ foreach (var row in reader)
✅ row[i], row.Count
✅ col.Span, col.Parse<T>()
```

**Migration effort:** Minimal for most users

---

## Definition of Done

### v3.0 Release Checklist

- [ ] All source code rewritten from scratch
- [ ] Zero unsafe code (`AllowUnsafeBlocks = false`)
- [ ] 100% test coverage on core logic
- [ ] Performance targets met (benchmarks prove it)
- [ ] Zero allocations verified (memory profiler)
- [ ] All audit issues resolved
- [ ] Documentation complete
- [ ] Examples in README
- [ ] NuGet package builds
- [ ] GitHub release with notes

---

## Timeline

| Phase | Duration | Deliverable |
|-------|----------|-------------|
| Phase 1: Foundation | 4-6 hours | Working parser + tests |
| Phase 2: SIMD | 8-12 hours | Performance optimization |
| Phase 3: Polish | 4-6 hours | Production ready |
| **Total** | **16-24 hours** | **v3.0 release** |

**Can be done in 2-3 focused days of work.**

---

## First Step: Clean Slate

```bash
# Archive old code
git mv src/HeroParser src/HeroParser.old
git mv tests tests.old

# Create new structure
mkdir -p src/HeroParser/Simd
mkdir -p tests/HeroParser.Tests

# Start fresh
# (Begin implementation)
```

---

## Questions to Resolve Before Starting

1. **Framework target:** net8.0 only, or net6.0+net8.0?
2. **Max columns:** 10,000 reasonable? Configurable?
3. **API surface:** Just the 3 Parse methods, or add helpers?
4. **Quote handling:** Future feature or permanently excluded?
5. **Package name:** Keep "HeroParser" or rename for v3?

---

## Success Criteria

### Must Have ✅
- ✅ 30+ GB/s on AVX-512
- ✅ Zero unsafe code
- ✅ Zero allocations in hot path
- ✅ All audit issues fixed
- ✅ Clean, maintainable code

### Nice to Have 🎯
- 🎯 Multi-framework support
- 🎯 Streaming file API
- 🎯 Parallel parsing
- 🎯 Quote handling

---

## Let's Build HeroParser v3.0! 🚀

**Ready to start?** Let me know and I'll begin the implementation.

**Prefer to adjust the plan?** Tell me what to change.

**Want to see a prototype first?** I can build Phase 1 (foundation) right now.
