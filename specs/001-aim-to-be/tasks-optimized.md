# Tasks: High-Performance CSV Parser (ROI-Optimized)

**Optimization**: Reduced from 42 → 22 tasks, focused on core value delivery
**Timeline**: 5.5 weeks (vs 10 weeks original)
**Resources**: 2 developers (vs 3 developers original)

## Executive Summary
This optimized implementation focuses on **CSV parsing excellence** to achieve >25 GB/s performance and zero allocations. Non-essential features (fixed-length parsing, advanced CI/CD, source generation) are deferred to post-MVP.

## Critical Success Metrics
1. **Performance**: >25 GB/s CSV parsing (exceed Sep's 21 GB/s)
2. **Memory**: Zero allocations for 99th percentile operations
3. **Compatibility**: netstandard2.0 through net10.0 support
4. **Usability**: Simple `Parse<T>(string)` API for immediate adoption

## Phase 1: Foundation (Week 1) - 3 tasks ✅ COMPLETED

- [x] **T001** Create multi-target project structure at `src/HeroParser/HeroParser.csproj`
  **ROI**: Essential foundation, enables all development
  **Implementation Guidance**: Reference `plan.md:192-204` for framework strategy
  **Sub-tasks**:
  1. ✅ Create csproj with `<TargetFrameworks>netstandard2.0;netstandard2.1;net6.0;net7.0;net8.0;net9.0;net10.0</TargetFrameworks>`
  2. ✅ Add conditional compilation from `research.md:114-149`
  3. ✅ Add polyfill packages for netstandard2.0: `System.Memory`, `System.Numerics.Vectors`
  **Success Criteria**: ✅ Multi-target build succeeds across all frameworks

- [x] **T003** [P] Initialize core test projects
  **ROI**: Testing infrastructure for TDD approach
  **Implementation Guidance**: Reference `plan.md:122-127` for test structure
  **Sub-tasks**:
  1. ✅ `tests/HeroParser.UnitTests/` - Fast isolated tests, xUnit v3
  2. ✅ `tests/HeroParser.BenchmarkTests/` - BenchmarkDotNet with competitor baselines
  **Success Criteria**: ✅ Test projects build and can run tests

- [x] **T004** [P] Configure basic CI pipeline at `.github/workflows/ci-build.yml`
  **ROI**: Automated testing, prevents regressions
  **Sub-tasks**:
  1. ✅ Multi-platform build: Windows x64, Linux x64, macOS x64
  2. ✅ Framework subset: netstandard2.0, net8.0, net10.0
  3. ✅ Automated test execution and NuGet package generation
  **Success Criteria**: ✅ CI builds and tests all target platforms

## Phase 2: Performance Baseline (Week 2) - 4 tasks

- [ ] **T008** [P] **CSV Parser API Contract Test** at `tests/HeroParser.UnitTests/Contracts/CsvParserApiTests.cs`
  **ROI**: Defines exact API surface, enables TDD
  **Implementation Guidance**: Reference `contracts/csv-parser-api.md:6-27`
  **Sub-tasks**:
  1. Simple sync APIs: `Parse(string)`, `Parse<T>(string)`, `ParseFile<T>()`
  2. Configuration API: `CsvParser.Configure().WithDelimiter().Build()`
  3. Error handling contracts: Exception hierarchy from `contracts/csv-parser-api.md:82-98`
  **Success Criteria**: Failing contract tests ready for TDD implementation

- [ ] **T010** [P] **BenchmarkDotNet baseline** at `tests/HeroParser.BenchmarkTests/BaselineBenchmarks.cs`
  **ROI**: Proves competitive advantage, validates core value prop
  **Implementation Guidance**: Reference `research.md:6-38` for targets
  **Sub-tasks**:
  1. Competitor baselines: Sep (21 GB/s), Sylvan.Data.Csv, CsvHelper
  2. Test datasets: 1KB, 1MB, 1GB for comprehensive measurement
  3. Performance targets: >25 GB/s (.NET 8), >30 GB/s (.NET 10)
  **Success Criteria**: Baseline shows current performance gap vs Sep

- [ ] **T011** [P] **Memory allocation profiling** at `tests/HeroParser.BenchmarkTests/MemoryBenchmarks.cs`
  **ROI**: Validates zero-allocation requirement
  **Implementation Guidance**: Reference `data-model.md:140-145`
  **Sub-tasks**:
  1. GC allocation tracking for 99th percentile operations
  2. Memory overhead measurement: <1KB per 1MB parsed target
  3. Large file constant memory usage validation
  **Success Criteria**: Memory baseline establishes allocation patterns

- [ ] **T012** [P] **RFC 4180 compliance test** at `tests/HeroParser.ComplianceTests/Rfc4180ComplianceTests.cs`
  **ROI**: Ensures standards compliance, prevents customer issues
  **Sub-tasks**:
  1. Core RFC specification: Field separation, quoting, record termination
  2. Edge cases: Embedded quotes, newlines in fields, empty fields
  3. Format variations: Different line endings, optional delimiters
  **Success Criteria**: Comprehensive compliance test suite

## Phase 3: Core Data Models (Week 3) - 4 tasks

- [ ] **T016** [P] **CsvRecord entity** at `src/HeroParser/Core/CsvRecord.cs`
  **ROI**: Foundation for zero-allocation field access
  **Implementation Guidance**: Reference `data-model.md:5-23`
  **Sub-tasks**:
  1. Core structure: `FieldCount`, `LineNumber`, `RawData: ReadOnlySpan<char>`, `FieldSpans: ReadOnlySpan<Range>`
  2. Zero-allocation field access: `GetField(int index): ReadOnlySpan<char>`
  3. Lazy parsing: Defer field extraction until `GetField()` call
  **Success Criteria**: Zero-allocation field access with lazy evaluation

- [ ] **T017** [P] **ParseResult<T> entity** at `src/HeroParser/Core/ParseResult.cs`
  **ROI**: Essential for error handling and diagnostics
  **Implementation Guidance**: Reference `data-model.md:53-65`
  **Sub-tasks**:
  1. Core structure: `Records: IEnumerable<T>`, `Errors: ParseError[]`, `Statistics`
  2. Lazy enumeration: Defer record materialization until enumerated
  3. Performance statistics: Throughput and memory usage tracking
  **Success Criteria**: Comprehensive diagnostics with lazy enumeration

- [ ] **T018** [P] **ParserConfiguration entity** at `src/HeroParser/Configuration/ParserConfiguration.cs`
  **ROI**: Immutable configuration for consistent behavior
  **Implementation Guidance**: Reference `data-model.md:37-52`
  **Sub-tasks**:
  1. Core options: Delimiter, QuoteCharacter, TrimWhitespace, EnableParallelProcessing
  2. Immutable design: All properties readonly after builder completion
  3. Builder pattern: Fluent API with validation during `Build()`
  **Success Criteria**: Immutable configuration with validated builder

- [ ] **T019** [P] **Basic type mapping** at `src/HeroParser/Mapping/BasicTypeMapper.cs`
  **ROI**: Essential for Parse<T>() functionality
  **Implementation Guidance**: Reference `data-model.md:101-106`
  **Sub-tasks**:
  1. Built-in types: int, long, decimal, DateTime, string, nullable types
  2. AOT-compatible type inspection: No reflection usage
  3. Simple field mapping: Index-based mapping for common scenarios
  **Success Criteria**: Basic type conversion without custom converters

## Phase 4: Memory & SIMD (Week 4) - 4 tasks

- [ ] **T020** [P] **CPU capability detection** at `src/HeroParser/Core/CpuOptimizations.cs`
  **ROI**: Enables hardware-specific optimizations
  **Implementation Guidance**: Reference `research.md:408-465`
  **Code Pattern**: Implement exact structure from `research.md:424-466`
  **Sub-tasks**:
  1. Framework-conditional detection: `#if NET6_0_OR_GREATER`
  2. Intel capabilities: AVX-512BW, AVX-512VL, GFNI support
  3. ARM64 detection: AdvSimd support, Apple Silicon identification
  **Success Criteria**: Runtime CPU detection enables optimal parser selection

- [ ] **T021** [P] **Memory pool implementation** at `src/HeroParser/Memory/BufferPool.cs`
  **ROI**: Zero-allocation buffer reuse
  **Implementation Guidance**: Reference `data-model.md:125-138`
  **Sub-tasks**:
  1. Thread-local pools: Small (64B-512B), Medium (1KB-8KB), Large (16KB-128KB)
  2. Power-of-2 sizing: SIMD-optimized buffer alignment
  3. Automatic cleanup: Buffer lifecycle management
  **Success Criteria**: Thread-local buffer pools with zero-allocation reuse

- [ ] **T022** [P] **Span extensions** at `src/HeroParser/Memory/SpanExtensions.cs`
  **ROI**: Zero-allocation string operations
  **Implementation Guidance**: Reference `data-model.md:140-145`
  **Sub-tasks**:
  1. Framework polyfills: netstandard2.0 compatibility
  2. SIMD character searching: Vectorized IndexOf for delimiters
  3. Zero-allocation operations: Span slicing without intermediate strings
  **Success Criteria**: Span operations eliminate string allocations

- [ ] **T023** **SIMD optimization engine** at `src/HeroParser/Core/SimdOptimizations.cs`
  **ROI**: Core performance differentiator
  **Dependencies**: T020 (CPU detection), T022 (Span extensions)
  **Implementation Guidance**: Reference `research.md:468-485`
  **Code Pattern**: Use exact conditional compilation from `research.md:497-521`
  **Sub-tasks**:
  1. Algorithm selection: `CreateOptimizedParser<T>()` pattern
  2. AVX-512 optimization: 512-bit vectorized parsing
  3. ARM NEON support: 128-bit vector operations for Apple Silicon
  4. Scalar fallback: netstandard2.0 compatibility
  **Success Criteria**: Runtime SIMD selection provides optimal performance

## Phase 5: Core Parser (Week 5) - 3 tasks

- [ ] **T024** **High-performance CSV parser** at `src/HeroParser/Core/CsvParser.cs`
  **ROI**: Primary value delivery - the actual parser
  **Dependencies**: T016 (CsvRecord), T021 (BufferPool), T023 (SIMD)
  **Implementation Guidance**: Reference `contracts/csv-parser-api.md:58-77`
  **Sub-tasks**:
  1. SIMD delimiter detection: Use T023 vectorized operations
  2. Zero-allocation enumeration: T016 CsvRecord with Span<Range> boundaries
  3. Parallel processing: Work-stealing queue for files >10MB
  4. Performance targets: >25 GB/s single-threaded, >50 GB/s multi-threaded
  **Success Criteria**: Parser achieves performance targets with zero allocations

- [ ] **T028** [P] **CSV options builder** at `src/HeroParser/Configuration/CsvOptions.cs`
  **ROI**: User-friendly configuration API
  **Sub-tasks**:
  1. Fluent API: `WithDelimiter()`, `WithQuoteChar()`, `EnableParallelProcessing()`
  2. Performance tuning: SIMD enable/disable, buffer size hints
  3. Validation: Ensure configuration consistency during build
  **Success Criteria**: Intuitive configuration with performance options

- [ ] **T030** **Parser builder integration** at `src/HeroParser/Configuration/ParserBuilder.cs`
  **ROI**: Unified entry point for configuration
  **Dependencies**: T028 (CsvOptions), T020 (CPU detection)
  **Sub-tasks**:
  1. Configuration assembly: Combine CSV options with hardware detection
  2. Parser instantiation: Select optimal parser based on capabilities
  3. Validation: Ensure configuration compatibility
  **Success Criteria**: Single builder creates optimized parser instances

## Phase 6: Public API & Validation (Week 6: 3 days) - 4 tasks

- [ ] **T035** **Simple synchronous APIs** at `src/HeroParser/CsvParser.cs`
  **ROI**: Primary user interface
  **Dependencies**: T024 (Parser core), T030 (Builder)
  **Implementation Guidance**: Reference `contracts/csv-parser-api.md:6-15`
  **Sub-tasks**:
  1. Static methods: `Parse(string)`, `Parse<T>(string)`, `ParseFile<T>()`
  2. Encoding detection: Automatic BOM detection
  3. Error handling: Exception hierarchy from API contract
  **Success Criteria**: Public API matches contract specifications

- [ ] **T038** [P] **Performance validation** at `tests/HeroParser.BenchmarkTests/ComprehensiveBenchmarks.cs`
  **ROI**: Proves competitive advantage
  **Sub-tasks**:
  1. Sep comparison: Demonstrate >25 GB/s performance
  2. Multi-framework validation: Performance across target frameworks
  3. Memory validation: Zero-allocation verification
  **Success Criteria**: Benchmarks prove >20% performance advantage over Sep

- [ ] **T042** **Competitive benchmark report** at `benchmarks/CompetitiveBenchmarkReport.md`
  **ROI**: Marketing material and validation
  **Dependencies**: T038 (Performance validation)
  **Sub-tasks**:
  1. Performance comparison: HeroParser vs Sep vs Sylvan vs CsvHelper
  2. Methodology documentation: Reproducible benchmark setup
  3. Hardware-specific results: Intel, AMD, ARM64 performance
  **Success Criteria**: Comprehensive performance comparison report

- [ ] **T999** **Basic documentation** at `README.md`
  **ROI**: Essential for adoption
  **Sub-tasks**:
  1. Quick start guide: Basic usage examples
  2. Performance claims: Benchmark results summary
  3. Installation: NuGet package instructions
  **Success Criteria**: Complete usage documentation

## Eliminated Tasks (Deferred to Post-MVP)

**Fixed-Length Features**: T013, T026, T029, T032, T034 (5 tasks)
- **Rationale**: CSV is 90% of market, focus on dominance first

**Advanced CI/CD**: T005-T007 (3 tasks)
- **Rationale**: Basic CI sufficient, advanced features are overhead

**Source Generation**: T002, T031 (2 tasks)
- **Rationale**: Manual type mapping sufficient for MVP

**Advanced Features**: T025, T027, T033, T036-T037, T039-T041 (8 tasks)
- **Rationale**: Nice-to-have features that delay core value delivery

## Success Validation

**MVP Definition**:
- ✅ >25 GB/s CSV parsing performance
- ✅ Zero allocations for 99th percentile
- ✅ Simple `Parse<T>(string)` API
- ✅ Multi-framework compatibility (netstandard2.0-net10.0)

**Resource Efficiency**:
- **Timeline**: 5.5 weeks (vs 10 weeks original)
- **Team Size**: 2 developers (vs 3 developers)
- **Task Count**: 22 tasks (vs 42 tasks original)
- **Risk**: Lower (focused scope, proven value)

This optimized approach delivers maximum value in minimum time while preserving strategic options for post-MVP enhancement based on market feedback.