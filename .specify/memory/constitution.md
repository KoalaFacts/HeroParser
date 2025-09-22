<!--
Sync Impact Report
==================
Version change: 1.0.0 → 2.1.1
Rationale: PATCH - Added experimental development protocol for innovation management
Modified principles:
- Code Quality Standards → Performance-First Architecture
- Testing Discipline → Benchmark-Driven Development
- RFC Compliance → RFC 4180 Strict Compliance
- User Experience Consistency → API Excellence & Consistency
- Zero-Allocation Awareness → Zero-Allocation Mandate
- Performance Requirements → World-Class Performance Targets
Added sections:
- Technical Decision Framework
- Implementation Prioritization
Removed sections: None
Templates requiring updates:
- .specify/templates/plan-template.md ✅ updated
- .specify/templates/spec-template.md ✅ updated
- .specify/templates/tasks-template.md ✅ updated
Follow-up TODOs:
- TODO(RATIFICATION_DATE): Set initial ratification date when adopted
-->

# HeroParser Constitution

## Core Principles

### I. Performance-First Architecture

Performance is the PRIMARY design constraint. Every architectural decision MUST
prioritize speed over convenience. Code MUST use unsafe contexts, SIMD
intrinsics, and hardware acceleration where beneficial. Struct layouts MUST be
cache-line optimized. Hot paths MUST avoid virtual dispatch, boxing, and
interface calls. Method inlining MUST be explicitly controlled via AggressiveInlining.
Cyclomatic complexity limits are WAIVED for performance-critical sections with
documented benchmarks showing >10% improvement.

### II. Benchmark-Driven Development

Every feature MUST begin with BenchmarkDotNet performance baselines comparing
against Sep (current leader at 21 GB/s), Sylvan.Data.Csv, CsvHelper, and
Addax.Formats.Tabular. Features without benchmarks showing superior performance
MUST NOT be merged. Microbenchmarks MUST cover: small files (1KB), medium files
(1MB), large files (1GB), streaming scenarios, and multi-threading with both
workstation and server GC. Memory diagnostics MUST track allocations, GC
collections, and working set. Performance regressions >2% require immediate reversion.

### III. RFC 4180 Strict Compliance

CSV parsing MUST fully comply with RFC 4180 while maintaining performance
leadership. Deviations are permitted ONLY for documented real-world compatibility
(Excel quirks, TSV variants) via opt-in configuration flags. Fixed-length format
MUST support COBOL copybook definitions, IBM mainframe formats, and NACHA
specifications. All format variations MUST be documented with example files
and compliance test suites.

### IV. API Excellence & Consistency

Public APIs MUST be intuitive for both simple and advanced use cases. Provide
both synchronous and asynchronous APIs with identical semantics. Support
Span<T>, Memory<T>, and PipeReader/Writer for zero-copy scenarios. MUST offer
fluent configuration builders, attribute-based mapping, and manual field
accessors. Breaking changes require major version bumps with migration tooling.

### V. Zero-Allocation Mandate

Parsing and writing MUST achieve zero heap allocations for common scenarios.
Stack allocation, ArrayPool<T>, and custom memory pools are REQUIRED. String
operations MUST use Span<char> or stackalloc for temporary buffers. Object
mapping MUST support source generators for allocation-free deserialization.
The 99th percentile parsing operation MUST show 0 Gen0/Gen1/Gen2 collections
in benchmarks.

### VI. World-Class Performance Targets

Performance targets are NON-NEGOTIABLE minimums to exceed current leader (Sep):
- Parse throughput: >25 GB/s single-threaded (vs Sep's 21 GB/s), >50 GB/s multi-threaded
- Write throughput: >20 GB/s single-threaded, >40 GB/s multi-threaded
- Memory overhead: <1KB per 1MB parsed (excluding user objects)
- Startup time: <1ms for first parse operation
- Large file support: Must handle 100GB+ files without degradation
- Multi-threaded advantage: >50x faster than CsvHelper (vs Sep's 35x)
MUST outperform Sep, Sylvan.Data.Csv, and CsvHelper by >20% in standard benchmarks.

## Technical Decision Framework

### Performance Trade-off Matrix

When principles conflict, apply this precedence:
1. **Correctness**: Data integrity is absolute (no silent corruption)
2. **Performance**: Speed is the primary differentiator
3. **Memory efficiency**: Zero-allocation is the standard
4. **API usability**: Power users over beginners
5. **Maintainability**: Accept complexity for measurable gains

### Implementation Prioritization

Features MUST be prioritized by performance impact:
- **P0**: Core parsing/writing hot paths (immediate focus)
- **P1**: Memory management and pooling (critical efficiency)
- **P2**: API surface and configuration (user experience)
- **P3**: Error handling and diagnostics (robustness)
- **P4**: Documentation and samples (adoption)

Any P0/P1 performance regression blocks ALL other work until resolved.

## Quality Assurance

### Benchmark Requirements
- Every PR MUST include benchmark results vs baseline
- Benchmark suite MUST run on: Windows, Linux, macOS (x64/ARM64)
- Results MUST include: throughput, allocations, CPU cycles, cache misses
- Comparative benchmarks against competitors MUST be maintained

### Testing Standards
- Unit tests for all edge cases and RFC compliance points
- Property-based testing for parser/writer round-trips
- Fuzzing for malformed input handling
- Integration tests with real-world CSV files >1GB
- Platform-specific tests for SIMD optimizations

### Performance Gates
- CI/CD MUST fail if performance regresses >2%
- Memory allocations MUST not exceed baseline
- Benchmark history MUST be tracked in git
- Release notes MUST highlight performance improvements

### Experimental Development Protocol
- Experiments and discovery tours are ENCOURAGED for breakthrough performance gains
- All experiments MUST begin with detailed research plan and success criteria
- Experiments MUST follow "fail fast" methodology with rapid feedback loops
- Successful trials MUST replace existing implementations before first public release
- Failed experiments MUST be completely reverted with lessons documented
- Experimental branches MUST include benchmark comparisons vs current implementation
- No experimental code MUST reach production without proving >20% performance improvement

## Governance

The Constitution defines our unwavering commitment to building the world's
fastest C# CSV/fixed-length parser. Performance is not a feature—it is THE
feature. All technical decisions MUST demonstrate measurable performance
benefits through benchmarks.

### Amendment Process
1. Proposed changes MUST include performance impact analysis
2. Benchmark data comparing before/after MUST be provided
3. Review period minimum 72 hours for performance-critical changes
4. Requires unanimous approval from performance team leads
5. Version increment per semantic versioning

### Compliance Verification
- Automated performance gates in CI/CD pipeline
- Weekly performance regression reports
- Quarterly competitive benchmark updates
- Annual third-party performance audit

### Decision Authority
Performance-critical decisions follow this hierarchy:
1. Benchmark data (empirical evidence trumps opinion)
2. Performance team consensus
3. Maintainer vote (2/3 majority)
4. Project lead veto (performance regressions only)

**Version**: 2.1.1 | **Ratified**: TODO(RATIFICATION_DATE) | **Last Amended**: 2025-09-22