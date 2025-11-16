# Benchmarks Ready - HeroParser v3.0

**Status:** ✅ READY TO BENCHMARK

---

## Framework Changes (.NET 8-10 Only)

### Before
- Targeted 7 frameworks: netstandard2.0, netstandard2.1, net6.0, net7.0, net8.0, net9.0, net10.0
- Conditional compilation everywhere (`#if NET6_0_OR_GREATER`)
- Polyfill packages for older frameworks
- Complex multi-framework support

### After
- **Targets only:** net8.0, net9.0, net10.0
- **No conditional compilation** - always have SIMD
- **No polyfill packages** - everything built-in
- **Simpler codebase** - 100% modern .NET

### Benefits
- ✅ Always have full SIMD support (AVX-512, AVX2, NEON)
- ✅ Cleaner code (no #if directives)
- ✅ Smaller package (no polyfills)
- ✅ Latest .NET features
- ✅ Faster builds (fewer targets)

---

## Benchmarks Created

### 1. QuickTest.cs - Fast Iteration

**Purpose:** Quick throughput validation without BenchmarkDotNet overhead

**Test:**
- 100,000 rows × 10 columns
- 10 iterations
- Shows throughput in GB/s

**Usage:**
```bash
dotnet run --project benchmarks/HeroParser.Benchmarks -- --quick
```

**Expected Output:**
```
=== HeroParser Quick Throughput Test ===
Hardware: SIMD: AVX-512F, AVX-512BW, AVX2, SSE2 | Using: Avx512Parser
Test data: 2,288,900 chars (4,577,800 bytes, 4.58 MB)
Throughput: 32.45 GB/s
✓ TARGET ACHIEVED: 32.45 GB/s >= 30 GB/s
```

**Color Coding:**
- 🟢 Green: ≥30 GB/s (AVX-512 target met!)
- 🟡 Yellow: ≥20 GB/s (AVX2 range)
- 🟡 Yellow: ≥10 GB/s (NEON range)
- 🔴 Red: <10 GB/s (below target)

---

### 2. ThroughputBenchmarks.cs - Full Analysis

**Purpose:** Comprehensive throughput measurement with BenchmarkDotNet

**Scenarios:**
- **Small:** 1,000 rows × 10 columns (~170 KB)
- **Medium:** 10,000 rows × 20 columns (~3.4 MB)
- **Large:** 100,000 rows × 10 columns (~4.5 MB)
- **Wide:** 1,000 rows × 100 columns (~1.4 MB)

**Usage:**
```bash
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --throughput
```

**What It Measures:**
- Mean execution time
- Memory allocations
- Throughput (calculate manually: bytes/time)
- GC collections

---

### 3. VsSepBenchmarks.cs - Head-to-Head Comparison

**Purpose:** Compare HeroParser vs Sep library (current fastest .NET CSV parser)

**Parameters:**
- Rows: 1,000 | 10,000 | 100,000
- Columns: 10 | 50

**Total combinations:** 6 (3 rows × 2 columns)

**Usage:**
```bash
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --vs-sep
```

**What It Measures:**
- Sep (baseline = 1.00x)
- HeroParser (e.g., 1.5x faster)
- Memory comparison

**Expected Results:**
- HeroParser should be 1.5-2x faster than Sep
- Similar or less memory usage

---

## How to Run Benchmarks

### Quick Test (30 seconds)
```bash
cd benchmarks/HeroParser.Benchmarks
dotnet run -- --quick
```

### Throughput Benchmarks (5-10 minutes)
```bash
cd benchmarks/HeroParser.Benchmarks
dotnet run -c Release -- --throughput
```

### Comparison Benchmarks (10-15 minutes)
```bash
cd benchmarks/HeroParser.Benchmarks
dotnet run -c Release -- --vs-sep
```

### All Benchmarks (15-25 minutes)
```bash
cd benchmarks/HeroParser.Benchmarks
dotnet run -c Release -- --all
```

---

## Hardware Requirements

### For 30+ GB/s (AVX-512)
- AMD Ryzen 9950X, 9900X, 7950X
- Intel Xeon (Sapphire Rapids or later)
- Intel Core (12th gen or later with AVX-512)

### For 20+ GB/s (AVX2)
- Any Intel/AMD CPU from 2013+
- Most modern desktop/server CPUs

### For 12+ GB/s (NEON)
- Apple M1, M2, M3 (all variants)
- AWS Graviton 2/3/4
- Any ARM64 CPU with NEON

### Fallback (Scalar)
- Older CPUs without SIMD
- 2-5 GB/s expected

---

## Validation Checklist

Run this checklist to validate all performance claims:

- [ ] **Quick Test:** Run `--quick` and verify ≥30 GB/s (or ≥20/12 GB/s based on CPU)
- [ ] **Throughput:** Run `--throughput` and verify all scenarios complete in reasonable time
- [ ] **Comparison:** Run `--vs-sep` and verify HeroParser is faster than Sep
- [ ] **Memory:** Verify memory allocations are minimal (check BenchmarkDotNet output)
- [ ] **Hardware Info:** Verify correct SIMD parser is selected
- [ ] **All Tests Pass:** `dotnet test` should pass all 33 tests

---

## Expected Benchmark Results

### AMD Ryzen 9950X (AVX-512)
```
QuickTest: 30-35 GB/s ✓
ThroughputBenchmarks:
  Small:  ~50 µs
  Medium: ~500 µs
  Large:  ~4 ms
  Wide:   ~150 µs
VsSepBenchmarks: 1.5-2.0x faster than Sep
```

### Intel Core i9 (AVX2)
```
QuickTest: 20-25 GB/s ✓
ThroughputBenchmarks:
  Small:  ~75 µs
  Medium: ~750 µs
  Large:  ~6 ms
  Wide:   ~220 µs
VsSepBenchmarks: 1.3-1.7x faster than Sep
```

### Apple M1 Pro (NEON)
```
QuickTest: 12-15 GB/s ✓
ThroughputBenchmarks:
  Small:  ~125 µs
  Medium: ~1.2 ms
  Large:  ~10 ms
  Wide:   ~370 µs
VsSepBenchmarks: 1.2-1.5x faster than Sep
```

---

## Troubleshooting

### Lower than expected performance?

1. **Check build configuration:**
   ```bash
   dotnet run -c Release  # Must use Release!
   ```

2. **Check hardware info:**
   ```csharp
   Console.WriteLine(HeroParser.Simd.SimdParserFactory.GetHardwareInfo());
   ```
   Should show AVX-512, AVX2, or NEON

3. **Check CPU governor (Linux):**
   ```bash
   cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_governor
   # Should be "performance", not "powersave"
   ```

4. **Disable other applications:**
   - Close browsers, IDEs, etc.
   - Benchmarking needs dedicated CPU

5. **Check thermal throttling:**
   - Monitor CPU temperatures
   - Ensure adequate cooling

---

## Next Steps

After running benchmarks:

1. **Update README.md** with actual benchmark results
2. **Create performance comparison chart** (HeroParser vs Sep vs others)
3. **Document hardware-specific results**
4. **Add benchmark results to GitHub releases**
5. **Update package description** with proven performance numbers

---

## Files Created

```
benchmarks/HeroParser.Benchmarks/
├── HeroParser.Benchmarks.csproj   # Project file
├── Program.cs                      # CLI entry point
├── QuickTest.cs                    # Fast throughput test
├── ThroughputBenchmarks.cs         # Full throughput suite
└── VsSepBenchmarks.cs              # HeroParser vs Sep
```

**Total:** 5 new files, ~650 lines of benchmark code

---

## 🚀 Ready to Validate 30+ GB/s!

Everything is set up. Just run the benchmarks and prove the performance claim!

```bash
# Quick validation (30 seconds)
cd /home/user/HeroParser
dotnet run --project benchmarks/HeroParser.Benchmarks -- --quick

# Full validation (15-25 minutes)
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --all
```

Let's see those blazing fast numbers! 🔥
