# HeroParser v3.0 - Zero-Allocation RFC 4180 Compliant CSV Parser

**High-Performance SIMD Parsing** | RFC 4180 Compliant | Zero Allocations

## 🚀 Key Features

- **RFC 4180 Compliant**: Full quote handling with escaped quotes (`""`)
- **Quote-Aware SIMD**: Maintains SIMD performance even with quoted fields
- **Zero Allocations**: Stack-only parsing with ArrayPool for column metadata
- **Lazy Evaluation**: Columns parsed only when accessed
- **Multi-Framework**: .NET 8, 9, and 10 support
- **Safe Memory**: No unsafe code - uses MemoryMarshal + Unsafe APIs

## 🎯 Design Philosophy

### Zero-Allocation, RFC-Compliant Design

- **Target Frameworks**: .NET 8, 9, 10 (modern JIT optimizations)
- **Memory Safety**: No unsafe code - safe MemoryMarshal + Unsafe APIs
- **Minimal API**: Simple, focused API surface
- **Zero Dependencies**: No external packages for core library
- **RFC 4180**: Full compliance with quote handling
- **SIMD First**: Quote-aware SIMD for AVX-512, AVX2, NEON

### API Surface

```csharp
// Primary API - parse from string with options
var reader = Csv.Parse(csvData);

// Custom options (delimiter, quote character, max columns)
var options = new CsvParserOptions
{
    Delimiter = ',',  // Default
    Quote = '"',      // Default - RFC 4180 compliant
    MaxColumns = 256  // Default
};
var reader = Csv.Parse(csvData, options);
```

## 📊 Usage Examples

### Basic Iteration (Zero Allocations)

```csharp
foreach (var row in Csv.Parse(csv))
{
    // Access columns by index - no allocations
    var id = row[0].Parse<int>();
    var name = row[1].Span; // ReadOnlySpan<char>
    var price = row[2].Parse<decimal>();
}
```

### Quote Handling (RFC 4180)

```csharp
var csv = "field1,\"field2\",\"field,3\"\n" +
          "aaa,\"b,bb\",ccc\n" +
          "zzz,\"y\"\"yy\",xxx";  // Escaped quote

foreach (var row in Csv.Parse(csv))
{
    // Access raw value (includes quotes)
    var raw = row[1].ToString(); // "b,bb"

    // Remove surrounding quotes and unescape
    var unquoted = row[1].UnquoteToString(); // b,bb

    // Zero-allocation unquote (returns span)
    var span = row[1].Unquote(); // ReadOnlySpan<char>
}
```

### Type Parsing

```csharp
foreach (var row in Csv.Parse(csv))
{
    // Generic parsing (ISpanParsable<T>)
    var value = row[0].Parse<int>();

    // Optimized type-specific methods
    if (row[1].TryParseDouble(out double d)) { }
    if (row[2].TryParseDateTime(out DateTime dt)) { }
    if (row[3].TryParseBoolean(out bool b)) { }
}
```

### Lazy Evaluation

```csharp
// Columns are NOT parsed until first access
foreach (var row in Csv.Parse(csv))
{
    // Skip rows without parsing columns
    if (ShouldSkip(row))
        continue;

    // Only parse columns when accessed
    var value = row[0].Parse<int>();  // First access triggers parsing
}
```

## 🔧 Architecture

### Quote-Aware SIMD Parsers

All SIMD parsers implement quote-aware parsing using bitmask techniques (inspired by Sep library):

1. **Avx512Parser** (Primary for Intel/AMD)
   - Processes 64 characters per iteration
   - Separate bitmasks for delimiters and quotes
   - Quote parity tracking: `(quoteCount & 1)` determines if inside quotes
   - Escaped quotes ("") automatically work: increment by 2, parity unchanged

2. **Avx2Parser** (Fallback for older CPUs)
   - Processes 32 characters per iteration
   - Same bitmask technique as AVX-512
   - Pack + permute for UTF-16 to byte conversion

3. **NeonParser** (ARM)
   - Processes 64 characters per iteration (8× 128-bit vectors)
   - Quote-aware SIMD for Apple Silicon
   - Optimized for M1/M2/M3 processors

4. **ScalarParser** (Baseline)
   - Character-by-character with RFC 4180 state machine
   - Correctness reference implementation

### Key Techniques

#### 1. Quote-Aware Bitmask Parsing
```csharp
// Load 64 chars, convert to bytes
// Create separate bitmasks for delimiters AND quotes
var delimiterMask = ExtractBitmask(compareDelimiter);
var quoteMask = ExtractBitmask(compareQuote);

// Process special characters sequentially
while (specialMask != 0)
{
    int bitPos = BitOperations.TrailingZeroCount(specialMask);

    if (IsQuote(bitPos))
        quoteCount++;  // Toggle quote state
    else if (IsDelimiter(bitPos) && (quoteCount & 1) == 0)
        RecordColumn();  // Only if outside quotes
}
```

#### 2. Quote Parity Tracking
```csharp
// Even quote count = outside quotes
// Odd quote count = inside quotes
bool insideQuotes = (quoteCount & 1) != 0;

// Escaped quotes ("") automatically work:
// - First quote: quoteCount++   (odd = inside)
// - Second quote: quoteCount++  (even = outside)
// - Parity unchanged!
```

#### 3. Zero-Allocation Design
```csharp
// ref struct: stack-only allocation
// ReadOnlySpan<char>: no string allocations
// ArrayPool: reused buffers for column metadata
// Lazy parsing: only parse when accessing columns
```

#### 4. Safe Memory Access
```csharp
// No unsafe keyword - uses safe APIs
ref readonly char start = ref MemoryMarshal.GetReference(line);
ref readonly char pos = ref Unsafe.Add(ref Unsafe.AsRef(in start), i);
var vec = Vector256.LoadUnsafe(ref Unsafe.As<char, ushort>(ref ...));
```

## 📦 Project Structure

```
src/HeroParser/
├── Csv.cs                            # Public API
├── CsvReader.cs                      # Main reader (ref struct)
├── CsvRow.cs                         # Row accessor (ref struct, lazy parsing)
├── CsvCol.cs                         # Column value (ref struct, Unquote methods)
├── CsvParserOptions.cs               # Configuration (delimiter, quote, max columns)
├── CsvException.cs                   # Error handling
└── Simd/
    ├── ISimdParser.cs                # Parser interface (quote-aware)
    ├── ScalarParser.cs               # Baseline with RFC 4180 state machine
    ├── Avx512Parser.cs               # Quote-aware SIMD for AVX-512
    ├── Avx2Parser.cs                 # Quote-aware SIMD for AVX2
    ├── NeonParser.cs                 # Quote-aware SIMD for ARM
    └── SimdParserFactory.cs          # Hardware detection

benchmarks/HeroParser.Benchmarks/
├── Program.cs                        # Benchmark launcher
├── QuickTest.cs                      # Fast throughput test
├── ThroughputBenchmarks.cs           # Single-threaded performance
├── VsSepBenchmarks.cs                # Head-to-head vs Sep
└── QuotedVsUnquotedBenchmarks.cs     # Verify quote-aware SIMD performance

tests/HeroParser.Tests/
├── BasicParsingTests.cs              # Core functionality tests
├── QuoteHandlingTests.cs             # Quote edge cases
└── Rfc4180Tests.cs                   # RFC 4180 compliance tests
```

## 🏗️ Building

**Requirements:**
- .NET 8, 9, or 10 SDK
- C# 12+ language features
- Recommended: AVX-512 or AVX2 capable CPU for maximum performance

```bash
# Build library
dotnet build src/HeroParser/HeroParser.csproj

# Run tests
dotnet test tests/HeroParser.Tests/HeroParser.Tests.csproj

# Quick throughput test (no BenchmarkDotNet overhead)
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --quick

# Compare with Sep library
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --vs-sep

# Verify quote-aware SIMD performance
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --quotes

# Run all benchmarks
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --all
```

## 📈 Benchmarking

### Quick Throughput Test

Fast iteration test without BenchmarkDotNet overhead:

```bash
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --quick
```

Output:
```
=== HeroParser Quick Throughput Test ===
Hardware: SIMD: AVX-512F, AVX-512BW, AVX2 | Using: Avx512Parser

Test data: 100,000 rows × 10 columns
Throughput: XX.XX GB/s

Expected: 20+ GB/s (AVX2), 30+ GB/s (AVX-512), 10+ GB/s (NEON)
```

### Quote Performance Verification

Verifies that quote-aware SIMD maintains performance:

```bash
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --quotes
```

This benchmark compares:
- **Unquoted CSV** (baseline) - pure SIMD fast path
- **Quoted CSV** (delimiters in quotes) - tests quote-aware SIMD
- **Mixed CSV** (50% quoted) - realistic scenario

Expected results if quote-aware SIMD works correctly:
- Unquoted should be fastest (baseline)
- Quoted should be only slightly slower (<20% overhead)
- Mixed should be between the two

If quoted is much slower (>50% overhead), quote-aware SIMD has issues.

## 🎯 Performance Goals

HeroParser aims to achieve competitive performance with Sep while maintaining:
- **RFC 4180 compliance** (quote handling)
- **Zero allocations** in hot path
- **Quote-aware SIMD** (no performance cliff on quoted data)

Performance targets (to be verified with benchmarks):
- **AVX-512**: Competitive with Sep's 21 GB/s on unquoted data
- **Quote overhead**: <20% slowdown on quoted data (vs pure unquoted)
- **Mixed workloads**: Between unquoted and quoted performance

## 🔬 Hardware Detection

Check SIMD capabilities:

```csharp
Console.WriteLine(HeroParser.Simd.SimdParserFactory.GetHardwareInfo());
// Output: "SIMD: AVX-512F, AVX-512BW, AVX2 | Using: Avx512Parser"
```

## ⚠️ Design Decisions

### RFC 4180 Quote Handling by Default
- All parsers support quoted fields with escaped quotes (`""`)
- Quote character configurable via `CsvParserOptions.Quote`
- Quote-aware SIMD maintains performance (minimal overhead)

### Lazy Column Parsing
- Columns not parsed until first access
- Allows efficient row filtering without parsing overhead
- ArrayPool buffers only rented when needed

### Framework Targeting
- .NET 8, 9, 10 only (no .NET Framework, no .NET 6/7)
- Leverages modern JIT optimizations and SIMD intrinsics
- Best AVX-512 and ARM NEON codegen

## 📊 Comparison: HeroParser vs Sep

| Feature | Sep | HeroParser v3.0 |
|---------|-----|-----------------|
| **RFC 4180 Compliance** | ✅ Full quote support | ✅ Full quote support |
| **Quote-Aware SIMD** | ✅ Bitmask technique | ✅ Bitmask technique (Sep-inspired) |
| **Zero Allocations** | ✅ ref structs | ✅ ref structs + ArrayPool |
| **Lazy Column Parsing** | ❌ | ✅ Parse on first access |
| **SIMD Paths** | AVX-512, AVX2, NEON | ✅ Same |
| **Memory Safety** | ✅ Safe only | ✅ Safe (MemoryMarshal + Unsafe) |
| **Framework Support** | .NET 6+ | .NET 8, 9, 10 |
| **External Dependencies** | csFastFloat | ✅ **Zero** |

## 🎉 Project Goals

### Core Principles
- ✅ **RFC 4180 Compliance**: Full quote handling with escaped quotes
- ✅ **Zero Allocations**: ref structs, ArrayPool, lazy parsing
- ✅ **Quote-Aware SIMD**: No performance cliff on quoted data
- ✅ **Zero Dependencies**: No external packages
- ✅ **Memory Safety**: No unsafe keyword (MemoryMarshal + Unsafe only)

### Performance Targets (To Be Verified)
- **Competitive with Sep**: Similar performance on unquoted data
- **Quote Overhead**: <20% slowdown on quoted data
- **Mixed Workloads**: Graceful performance between unquoted and quoted

### Testing
- ✅ **RFC 4180 Compliance**: Comprehensive quote handling tests
- ✅ **SIMD Correctness**: All parsers produce same results
- ✅ **Performance Verification**: Benchmarks for quote-aware SIMD

## 📝 License

MIT

## 🙏 Credits

Inspired by and built upon research from:
- **Sep** by nietras - Bitmask quote-aware SIMD technique
- **Sylvan** - Alternative high-performance CSV parsing approach
- **SimdUnicode** - SIMD validation and processing techniques

HeroParser implements Sep's bitmask technique for quote-aware SIMD parsing while adding:
- Lazy column evaluation for zero allocations in filtering scenarios
- .NET 8-10 targeting for latest JIT optimizations
- Zero external dependencies

Built to be a **competitive, RFC 4180 compliant, zero-allocation CSV parser for .NET**! 🚀
