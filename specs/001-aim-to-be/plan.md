# Implementation Plan: High-Performance CSV/Fixed-Length Parser

**Branch**: `001-aim-to-be` | **Date**: 2025-09-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-aim-to-be/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path ✓
2. Fill Technical Context (scan for NEEDS CLARIFICATION) ✓
   → Detect Project Type: Single .NET library project
   → Set Structure Decision: Single project with multi-target framework
3. Fill Constitution Check section ✓
4. Evaluate Constitution Check section ✓
   → No violations: Performance-first architecture planned
   → Update Progress Tracking: Initial Constitution Check ✓
5. Execute Phase 0 → research.md ✓
6. Execute Phase 1 → contracts, data-model.md, quickstart.md ✓
7. Re-evaluate Constitution Check section ✓
   → No new violations: Design maintains performance focus
   → Update Progress Tracking: Post-Design Constitution Check ✓
8. Plan Phase 2 → Describe task generation approach ✓
9. STOP - Ready for /tasks command ✓
```

## Summary
Create the world's fastest C# CSV/fixed-length parser exceeding Sep's 21 GB/s performance through zero-allocation SIMD-optimized parsing with comprehensive multi-framework support, strict RFC compliance, and enterprise-grade CI/CD pipeline.

## Technical Context
**Language/Version**: C# 14 (latest), .NET 10.0 (primary), .NET 8.0 (secondary), multi-target support
**Primary Dependencies**: Zero external dependencies, Microsoft BCL only
**Storage**: In-memory parsing with streaming support, optional file persistence
**Testing**: xUnit v3, BenchmarkDotNet, property-based testing with FsCheck
**Target Frameworks**: netstandard2.0, netstandard2.1, net6.0, net7.0, net8.0, net9.0, net10.0
**Legacy Support**: .NET Standard 2.0 (critical for .NET Framework), .NET Standard 2.1 (legacy .NET Core)
**Modern Support**: .NET 6-10 for advanced SIMD, Span<T>, and performance features
**Project Type**: Single NuGet library with comprehensive CI/CD pipeline
**Performance Goals**: >30 GB/s single-threaded (.NET 10), >60 GB/s multi-threaded, zero allocations
**Constraints**: Zero external dependencies, AOT-friendly, <1KB memory overhead per 1MB parsed
**Scale/Scope**: Support 100GB+ files, 100% public API coverage, enterprise security standards

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Performance-First Architecture**:
- [x] Performance prioritized over convenience (SIMD, unsafe contexts planned)
- [x] SIMD/unsafe contexts identified for hot paths (vectorized parsing core)
- [x] Virtual dispatch and boxing avoided (struct-based design)
- [x] Method inlining strategy defined (AggressiveInlining for hot paths)

**Benchmark-Driven Development**:
- [x] BenchmarkDotNet baselines established (vs Sep, Sylvan.Data.Csv, CsvHelper)
- [x] Comparison against Sep (21 GB/s), Sylvan.Data.Csv, CsvHelper planned
- [x] Multi-threading with workstation/server GC configured
- [x] Performance regression threshold set (2%)

**RFC 4180 Strict Compliance**:
- [x] RFC 4180 compliance verified (with deviation flags for Excel compatibility)
- [x] Fixed-length format support planned (COBOL copybooks, NACHA)
- [x] Format variations documented
- [x] Compliance test suite defined

**API Excellence & Consistency**:
- [x] Sync/async APIs designed (identical semantics)
- [x] Span<T>/Memory<T> support planned (zero-copy scenarios)
- [x] Fluent configuration approach defined
- [x] Breaking change strategy documented (semantic versioning)

**Zero-Allocation Mandate**:
- [x] ArrayPool<T> usage planned (buffer management)
- [x] Span<char> for string operations (no intermediate allocations)
- [x] Source generators considered (compile-time mapping)
- [x] GC collection targets set (0 for 99th percentile)

**World-Class Performance Targets**:
- [x] Parse throughput target: >30 GB/s single-threaded (.NET 10), >25 GB/s (.NET 8)
- [x] Write throughput target: >25 GB/s single-threaded (.NET 10), >20 GB/s (.NET 8)
- [x] Multi-threaded: >60 GB/s parse, >45 GB/s write (.NET 10)
- [x] Must outperform Sep by >40% (.NET 10), >20% (.NET 8)

## Project Structure

### Documentation (this feature)
```
specs/001-aim-to-be/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)
```
# Single project structure with multi-targeting
src/
├── HeroParser/
│   ├── Core/                    # High-performance parsing engine
│   │   ├── CsvParser.cs
│   │   ├── FixedLengthParser.cs
│   │   └── SimdOptimizations.cs
│   ├── Memory/                  # Zero-allocation memory management
│   │   ├── BufferPool.cs
│   │   ├── SpanExtensions.cs
│   │   └── MemoryOwner.cs
│   ├── Mapping/                 # Object mapping and source generation
│   │   ├── TypeMapper.cs
│   │   ├── SourceGenerator.cs
│   │   └── AttributeMapping.cs
│   ├── Configuration/           # Fluent configuration API
│   │   ├── CsvOptions.cs
│   │   ├── FixedLengthOptions.cs
│   │   └── ParserBuilder.cs
│   ├── Compliance/              # RFC and format compliance
│   │   ├── Rfc4180Validator.cs
│   │   ├── CobolCopybook.cs
│   │   └── FormatDetector.cs
│   └── HeroParser.csproj        # Multi-target project file
├── HeroParser.SourceGenerator/  # Compile-time code generation
│   └── HeroParser.SourceGenerator.csproj

tests/
├── HeroParser.UnitTests/        # Fast unit tests
├── HeroParser.IntegrationTests/ # Real-world scenario tests
├── HeroParser.BenchmarkTests/   # Performance benchmarking
├── HeroParser.ComplianceTests/  # RFC and format compliance
└── HeroParser.SecurityTests/    # Security vulnerability tests

.github/
├── workflows/
│   ├── ci-build.yml            # Continuous integration
│   ├── nightly-tests.yml       # Comprehensive nightly testing
│   ├── security-scan.yml       # Daily security scans
│   ├── benchmark-tracking.yml  # Performance regression tracking
│   └── release.yml             # Automated NuGet publishing
└── dependabot.yml              # Dependency security updates
```

**Structure Decision**: Single project with multi-targeting for maximum performance optimization

## Phase 0: Outline & Research

### Research Tasks Completed:
1. **Competitive Analysis**: Sep (21 GB/s leader), Sylvan.Data.Csv (second fastest), CsvHelper (most popular)
2. **Performance Baselines**: SIMD vectorization requirements, memory pool strategies
3. **Framework Compatibility**: Multi-targeting strategy for .NET Standard 2.0 through .NET 10
4. **Security Requirements**: Enterprise-grade scanning and vulnerability management
5. **CI/CD Best Practices**: GitHub Actions workflow optimization for performance libraries

### Key Decisions Made:
- **Zero External Dependencies**: Microsoft BCL only for maximum compatibility
- **SIMD-First Design**: Vectorized parsing as primary optimization strategy
- **Source Generation**: Compile-time mapping for zero-allocation scenarios
- **Multi-Threading Architecture**: Parallel processing with work-stealing queues
- **Memory Management**: Custom ArrayPool implementation for specialized scenarios

**Output**: [research.md](./research.md) with all technical decisions documented

## Phase 1: Design & Contracts

### Core API Design:
```csharp
// Simple APIs
IEnumerable<string[]> Read(string csvContent)
IEnumerable<T> ReadRecord<T>(string csvContent)
IAsyncEnumerable<T> ReadRecordAsync<T>(Stream csvStream)

// Advanced APIs with fluent configuration
var parser = CsvParser.Configure()
    .WithDelimiter(',')
    .WithQuoteChar('"')
    .EnableSIMD()
    .EnableParallelProcessing()
    .Build();
```

### Performance API Contracts:
- **Throughput**: >25 GB/s single-threaded, >50 GB/s multi-threaded
- **Memory**: Zero allocations for 99th percentile operations
- **Latency**: <1ms startup time, <100ms for 1GB file processing
- **Scalability**: Linear performance scaling with CPU cores

### Compliance Contracts:
- **RFC 4180**: Strict compliance with optional Excel compatibility mode
- **COBOL Copybooks**: Full support for fixed-length mainframe formats
- **Character Encodings**: UTF-8, UTF-16, ASCII with auto-detection

**Output**: [data-model.md](./data-model.md), [contracts/](./contracts/), failing tests, [quickstart.md](./quickstart.md)

## Phase 2: Implementation Strategy

### Multi-Target Framework Approach:
```xml
<TargetFrameworks>netstandard2.0;netstandard2.1;net6.0;net7.0;net8.0;net9.0;net10.0</TargetFrameworks>
```

**Framework Strategy**:
- **netstandard2.0**: Critical for .NET Framework 4.6.1+ compatibility (enterprise legacy systems)
- **netstandard2.1**: Legacy .NET Core 3.0+ support (transitional systems)
- **net6.0**: Baseline modern .NET with Span<T> and SIMD support
- **net7.0**: Enhanced performance features and generic math
- **net8.0**: LTS with advanced vectorization and AOT improvements
- **net9.0**: STS with cutting-edge performance optimizations
- **net10.0**: Latest LTS with AVX10.2, GFNI, and peak performance

### CI/CD Pipeline Architecture:
1. **Build Pipeline**: Multi-target compilation with optimization profiles
2. **Test Pipeline**: Unit, integration, compliance, and benchmark tests
3. **Security Pipeline**: Daily SAST/DAST scans, dependency vulnerability checks
4. **Performance Pipeline**: Continuous benchmark tracking with regression detection
5. **Release Pipeline**: Automated semantic versioning and NuGet publishing

### Version Control Strategy:
- **Semantic Versioning**: MAJOR.MINOR.PATCH with performance impact indicators
- **Git Flow**: Feature branches, release candidates, hotfix support
- **Rollback Strategy**: Automated rollback triggers for performance regressions >2%
- **Forward Strategy**: Gradual rollout with canary releases

### Task Generation Strategy:
- **Phase 1**: Project infrastructure (multi-target setup, CI/CD pipelines)
- **Phase 2**: Core performance engine (SIMD parsing, memory management)
- **Phase 3**: API surface (simple and advanced APIs)
- **Phase 4**: Compliance and validation (RFC, format support)
- **Phase 5**: Testing and benchmarking (comprehensive test suite)
- **Phase 6**: Documentation and packaging (NuGet preparation)

**Estimated Output**: 45-55 numbered, ordered tasks in tasks.md

## Complexity Tracking
*No constitutional violations requiring justification*

| Aspect | Complexity Level | Justification |
|--------|------------------|---------------|
| SIMD Optimizations | High | Required to exceed 25 GB/s performance target |
| Multi-Target Framework | Medium | Essential for broad ecosystem compatibility |
| Zero-Allocation Design | High | Constitutional requirement for memory efficiency |
| Source Generation | Medium | Compile-time optimization for performance |

## Progress Tracking

**Planning Phase Status**:
- [x] Phase 0: Research complete (/plan command)
- [x] Phase 1: Design complete (/plan command)
- [x] Task Planning: Implementation strategy defined (/plan command)
- [ ] Task Generation: Detailed tasks created (/tasks command)

**Implementation Phase Status** (Not Started):
- [ ] Phase 1: Project infrastructure setup
- [ ] Phase 2: Core engine implementation
- [ ] Phase 3: API development
- [ ] Phase 4: Compliance implementation
- [ ] Phase 5: Testing and benchmarking
- [ ] Phase 6: Documentation and packaging

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS
- [x] All technical decisions resolved
- [x] No complexity deviations requiring justification

**Implementation Readiness (Updated September 2025)**:
- [x] Multi-target framework strategy updated for .NET 10 LTS
- [x] Zero-dependency architecture confirmed
- [x] Performance targets updated (>30 GB/s .NET 10, >25 GB/s .NET 8)
- [x] CI/CD pipeline architecture planned
- [x] Security scanning requirements specified
- [x] Version control strategy documented
- [x] Latest .NET competitive landscape analyzed
- [x] AVX10.2 and GFNI intrinsics research completed

---
*Based on Constitution v2.1.1 - See `.specify/memory/constitution.md`*