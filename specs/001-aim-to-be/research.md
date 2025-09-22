# Research: High-Performance CSV/Fixed-Length Parser

**Date**: 2025-01-25 | **Phase**: 0 | **Status**: Complete

## Performance Optimization Research

### SIMD Optimization Strategy

**Decision**: Use System.Runtime.Intrinsics with hardware-specific optimizations (AVX-512, AVX2, ARM NEON)

**Rationale**:
- Sep (current leader at 21 GB/s) likely uses SIMD for character scanning
- Vector operations can process 16-32 characters simultaneously vs 1 character per cycle
- Critical for delimiter detection, quote handling, and newline scanning

**Alternatives Considered**:
- Software-only optimization: 5-10x slower than SIMD
- Unsafe pointer arithmetic: Good but limited vs vectorized operations
- Third-party SIMD libraries: Adds dependencies (violates constitution)

**Implementation Strategy**:
- Runtime hardware detection and fallback paths
- Hot path optimization for delimiter scanning using Vector<byte>
- Quoted field detection with vectorized compare operations

### Memory Management Architecture

**Decision**: Custom memory pools with ArrayPool<T> and stack allocation for hot paths

**Rationale**:
- Zero allocation mandate requires careful memory management
- Large file parsing needs buffer reuse to avoid GC pressure
- Stack allocation for temporary buffers <1KB

**Alternatives Considered**:
- Native memory allocation: Complex, potential leaks
- Standard string operations: High allocation rate
- MemoryMappedFiles: Good for very large files but complexity overhead

**Implementation Strategy**:
- ArrayPool<char> for parsing buffers
- Span<char> for all string operations
- stackalloc for small temporary buffers
- Custom memory pool for parsed record storage

### Parsing Algorithm Research

**Decision**: Hybrid approach with fast path for simple CSV and fallback for complex cases

**Rationale**:
- 80/20 rule: Most CSV files are simple format without quotes/escapes
- Fast path can use SIMD scanning for delimiters
- Complex path handles RFC 4180 edge cases correctly

**Alternatives Considered**:
- State machine parser: Accurate but slower due to branching
- Regex-based parsing: Clean code but poor performance
- Character-by-character: Simple but not competitive

**Implementation Strategy**:
- Fast path: SIMD delimiter scanning, direct field extraction
- Fallback path: State machine for quoted fields and escapes
- Heuristic detection of CSV complexity to choose path

## Competitive Analysis

### Sep (Current Leader - 21 GB/s)

**Strengths**:
- Excellent SIMD optimization
- Zero-allocation design
- Modern C# performance patterns

**Opportunities for HeroParser**:
- Potential for better SIMD utilization
- Multi-threading improvements
- More comprehensive API surface

### Sylvan.Data.Csv

**Strengths**:
- Good API design
- Comprehensive feature set
- Strong RFC compliance

**Weaknesses**:
- Performance gap vs Sep
- More allocations than optimal

### CsvHelper

**Strengths**:
- Very popular, feature-rich
- Excellent documentation
- Flexible configuration

**Weaknesses**:
- Significant performance gap (35x slower than Sep multi-threaded)
- High allocation rate
- Reflection-heavy approach

## RFC 4180 Compliance Research

**Decision**: Full RFC 4180 compliance with opt-in extensions for real-world compatibility

**Critical Requirements**:
- CRLF as line separator (with LF tolerance)
- Double quote escaping within quoted fields
- Trailing commas handled correctly
- Empty fields and null handling

**Extensions Needed**:
- Excel CSV quirks (opt-in)
- Custom delimiters beyond comma
- Comment line support (# prefix)
- Trimming whitespace (opt-in)

## Multi-Threading Strategy

**Decision**: Work-stealing approach with parallel field processing

**Rationale**:
- Large files can be chunked at line boundaries
- Each thread processes independent chunks
- Lock-free result aggregation

**Implementation Considerations**:
- Line boundary detection for safe chunking
- NUMA-aware memory allocation
- Optimal thread count based on hardware

## Source Generator Architecture

**Decision**: Compile-time reflection replacement for object mapping

**Benefits**:
- Zero allocation object construction
- Compile-time field mapping validation
- Performance equivalent to hand-written code

**Implementation Strategy**:
- Analyze target types at compile time
- Generate optimized field setters
- Support for custom conversion functions
- Integration with property naming conventions

## Testing Strategy

**Decision**: Multi-layered testing with property-based and performance regression tests

**Test Categories**:
1. **Unit Tests**: RFC 4180 compliance, edge cases
2. **Property Tests**: Round-trip parsing consistency
3. **Performance Tests**: Regression detection (<2%)
4. **Integration Tests**: Large file handling (>1GB)
5. **Fuzzing Tests**: Malformed input robustness

**Benchmark Infrastructure**:
- BenchmarkDotNet for accurate measurements
- Multiple data set sizes (1KB, 1MB, 1GB)
- Competitor comparison automation
- CI/CD integration for regression detection

## Framework Compatibility Research

**Decision**: Multi-targeting with framework-specific optimizations

**Framework-Specific Features**:
- net6.0+: IAsyncEnumerable, newer SIMD APIs
- netstandard2.0: Compatibility layer for older APIs
- Performance testing across all targets

**API Surface Decisions**:
- Conditional compilation for async features
- Span<T> support where available
- Fallback implementations for older frameworks

## File Format Extensions

**Fixed-Length Format Requirements**:
- COBOL copybook parsing and field definitions
- IBM mainframe format support (EBCDIC considerations)
- NACHA ACH file format compliance
- Custom field layout APIs

**Research Findings**:
- COBOL copybook: PIC clauses, COMP fields, redefines
- IBM formats: Fixed/variable record lengths, blocked records
- NACHA: Specific field positions and validation rules

## Error Handling Strategy

**Decision**: Performance-optimized exception handling with detailed diagnostics

**Approach**:
- Fast path with minimal error checking
- Detailed error reporting for malformed data
- Structured exceptions with position information
- Optional strict vs lenient parsing modes

**Exception Types**:
- CsvParseException: Malformed CSV structure
- CsvMappingException: Type conversion failures
- CsvConfigurationException: Invalid parser setup

## Conclusion

All technical uncertainties resolved. Ready for Phase 1 design and contract generation. The research establishes a clear technical foundation for achieving >25 GB/s parsing performance through SIMD optimization, zero-allocation memory management, and multi-threading while maintaining full RFC 4180 compliance.