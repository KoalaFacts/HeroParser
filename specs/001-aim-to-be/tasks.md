# Tasks: HeroParser - 4 Major Features with Complete Cycles

**Input**: Design documents from `/specs/001-aim-to-be/`
**Prerequisites**: plan.md (✓), research.md (✓), data-model.md (✓), contracts/ (✓), quickstart.md (✓)

## Feature Breakdown Strategy
**4 Major Features - Each with Complete Implement → Test → Benchmark → Decision Cycles:**

1. **Feature 1: CSV Reading** - Core parsing with all optimizations (SIMD, memory, etc.)
2. **Feature 2: Fixed-Length Reading** - COBOL, IBM, NACHA format support
3. **Feature 3: CSV Writing** - High-performance writing with same optimizations
4. **Feature 4: Fixed-Length Writing** - Complete the library with writing support

**Each feature**: Setup → Cycles (implement → test → benchmark → decision) → Complete or DevOps

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- All file paths are absolute and based on C# NuGet library structure

# FEATURE 1: CSV READING (Current Focus)

## Feature 1 Setup

- [x] **T001** Create C# NuGet library solution structure: `src/HeroParser/`, `tests/`, `HeroParser.sln`
- [x] **T002** Initialize main library project `src/HeroParser/HeroParser.csproj` with multi-framework targeting (netstandard2.0, netstandard2.1, net6.0, net7.0, net8.0, net9.0, net10.0)
- [x] **T003** [P] Create test projects: `HeroParser.Tests`, `HeroParser.IntegrationTests`, `HeroParser.PerformanceTests`, `HeroParser.ComplianceTests`
- [x] **T004** [P] Create `HeroParser.Benchmarks` project and `Directory.Build.props` for shared MSBuild properties
- [x] **T005** [P] Setup NuGet package metadata, versioning, and `nuget.config` for package management

## F1 Cycle 1: Basic CSV Reading Foundation

**Implement → Test → Benchmark → DECISION POINT**
*Focus: Get basic CSV reading working with minimal functionality*

### Implementation (T006-T010)
- [ ] **T006** [P] Create CSV-specific exception classes in `src/HeroParser/Exceptions/CsvParseException.cs` and `CsvMappingException.cs`
- [ ] **T007** Create basic CsvConfiguration in `src/HeroParser/Configuration/CsvConfiguration.cs` (delimiter, quote, escape for reading)
- [ ] **T008** Create minimal CsvParser static class in `src/HeroParser/Core/CsvParser.cs` (basic Parse(string) method for reading only)
- [ ] **T009** Create CsvParserBuilder basic fluent API in `src/HeroParser/Configuration/CsvParserBuilder.cs` (reading configuration only)
- [ ] **T010** Create ICsvParser interface in `src/HeroParser/Core/ICsvParser.cs` (reading methods only)

### Test (T011-T013)
- [ ] **T011** [P] Create basic CSV reading tests in `tests/HeroParser.Tests/Core/CsvParserTests.cs`
- [ ] **T012** [P] Create CsvParserBuilder tests in `tests/HeroParser.Tests/Configuration/CsvParserBuilderTests.cs`
- [ ] **T013** [P] Create basic RFC 4180 reading compliance tests in `tests/HeroParser.ComplianceTests/CsvReadingComplianceTests.cs`

### Benchmark & Analyze (T014-T015)
- [ ] **T014** Create CSV reading baseline benchmark vs Sep, Sylvan.Data.Csv, CsvHelper in `src/HeroParser.Benchmarks/CsvReadingCycle1Benchmarks.cs`
- [ ] **T015** Execute CSV reading benchmarks, document baseline performance, identify reading bottlenecks

### 🛑 **DECISION POINT**: Continue F1 Cycle 2 OR move to Feature 1 DevOps?
- Review CSV reading benchmark results and determine next focus
- If reading performance acceptable → Feature 1 DevOps (or move to Feature 2)
- If needs reading optimization → Continue to F1 Cycle 2

## F1 Cycle 2: Enhanced CSV Reading + Memory Optimization

**Implement → Test → Benchmark → DECISION POINT**
*Focus: Optimize CSV reading performance with memory management*

### Implementation (T016-T020)
- [ ] **T016** Enhance CsvParser with file reading, typed reading, async reading support
- [ ] **T017** [P] Implement ArrayPool<T> memory management in `src/HeroParser/Memory/MemoryPool.cs`
- [ ] **T018** [P] Implement Span<char> string operations in `src/HeroParser/Core/SpanOperations.cs`
- [ ] **T019** Refactor CsvParser to use memory pools and Span<char> for reading
- [ ] **T020** Add basic unsafe optimizations to hot CSV reading paths

### Test (T021-T023)
- [ ] **T021** [P] Create CSV reading contract tests from `contracts/csv-parser-api.md` in `tests/HeroParser.Tests/Contracts/CsvReadingApiTests.cs`
- [ ] **T022** [P] Create memory allocation tests for CSV reading in `tests/HeroParser.PerformanceTests/CsvReadingAllocationTests.cs`
- [ ] **T023** [P] Expand RFC 4180 reading compliance tests for enhanced CSV reading features

### Benchmark & Analyze (T024-T025)
- [ ] **T024** Create CSV reading cycle 2 benchmark suite with memory profiling in `src/HeroParser.Benchmarks/CsvReadingCycle2Benchmarks.cs`
- [ ] **T025** Execute CSV reading benchmarks, compare vs Cycle 1, validate memory optimizations for reading

### 🛑 **DECISION POINT**: Continue F1 Cycle 3 OR move to Feature 1 DevOps?
- Review CSV reading benchmark improvements and bottleneck analysis
- If reading performance acceptable → Feature 1 DevOps (or move to Feature 2)
- If needs SIMD optimization for reading → Continue to F1 Cycle 3

## F1 Cycle 3: CSV Reading SIMD Optimization

**Implement → Test → Benchmark → DECISION POINT**
*Focus: Maximum CSV reading performance with SIMD intrinsics*

### Implementation (T026-T028)
- [ ] **T026** Add SIMD intrinsics and hardware detection to CsvParser reading core
- [ ] **T027** Implement cache-line optimized CSV reading algorithms
- [ ] **T028** Advanced CSV reading optimizations (vectorized delimiter scanning, quote handling)

### Test (T029-T030)
- [ ] **T029** [P] Create platform-specific SIMD tests for CSV reading in `tests/HeroParser.PerformanceTests/CsvReadingSimdTests.cs`
- [ ] **T030** [P] Create comprehensive CSV reading performance tests for constitutional targets

### Benchmark & Analyze (T031-T032)
- [ ] **T031** Create CSV reading cycle 3 benchmark with SIMD validation in `src/HeroParser.Benchmarks/CsvReadingCycle3Benchmarks.cs`
- [ ] **T032** Execute CSV reading benchmarks, validate constitutional performance targets (>25 GB/s single-threaded reading)

### 🛑 **DECISION POINT**: Continue F1 Cycle N OR move to Feature 1 DevOps?
- Review if CSV reading constitutional targets met (>25 GB/s, >20% vs Sep for reading)
- If CSV reading targets met → Feature 1 DevOps (or move to Feature 2)
- If want more CSV reading optimizations → Continue to F1 Cycle 4

## F1 Cycle N: Advanced CSV Reading Features (If Needed)

**Implement → Test → Benchmark → DECISION POINT**
*Focus: Advanced CSV reading features like source generators, streaming*

### Implementation (T033-T035)
- [ ] **T033** [P] Implement source generator for allocation-free CSV reading mapping in `src/HeroParser/SourceGenerators/CsvReadingMappingGenerator.cs`
- [ ] **T034** [P] Implement PipeReader integration for CSV reading in `src/HeroParser/Streaming/CsvPipeReader.cs`
- [ ] **T035** Add advanced async patterns and streaming optimizations for CSV reading

### Test (T036-T037)
- [ ] **T036** [P] Create CSV reading source generator tests in `tests/HeroParser.Tests/SourceGenerators/CsvReadingMappingGeneratorTests.cs`
- [ ] **T037** [P] Create CSV reading streaming integration tests in `tests/HeroParser.IntegrationTests/CsvReadingStreamingTests.cs`

### Benchmark & Analyze (T038-T039)
- [ ] **T038** Create final CSV reading feature benchmark in `src/HeroParser.Benchmarks/CsvReadingFinalBenchmarks.cs`
- [ ] **T039** Execute comprehensive CSV reading performance validation across all features

### 🛑 **DECISION POINT**: Continue CSV Reading OR move to Feature 1 DevOps?
- Review CSV reading feature completeness and performance
- Could continue with more CSV reading cycles if needed
- Or move to Feature 1 DevOps when satisfied with CSV reading

## Feature 1 DevOps: CSV Reading Production Ready

**Goal: CSV Reading feature polished and ready for production**

- [ ] **T040** [P] Setup CI/CD pipeline for CSV reading with automated testing and benchmarking
- [ ] **T041** [P] Configure performance regression detection for CSV reading (<2% tolerance)
- [ ] **T042** [P] Create comprehensive CSV reading benchmark comparison report vs Sep, Sylvan.Data.Csv, CsvHelper
- [ ] **T043** [P] Update documentation with CSV reading performance results and API docs
- [ ] **T044** [P] Create sample project demonstrating CSV reading usage and performance
- [ ] **T045** **FEATURE 1 COMPLETE**: CSV Reading ready for production use

---

# FEATURE 2: FIXED-LENGTH READING (Next Major Feature)

*After Feature 1 is complete, start new branch/cycle for Fixed-Length Reading*
*Same pattern: Setup → Cycles → DevOps*

# FEATURE 3: CSV WRITING (Future Major Feature)

*After Feature 2 is complete, start new branch/cycle for CSV Writing*
*Same pattern: Setup → Cycles → DevOps*

# FEATURE 4: FIXED-LENGTH WRITING (Final Major Feature)

*After Feature 3 is complete, start new branch/cycle for Fixed-Length Writing*
*Same pattern: Setup → Cycles → DevOps*

## Final Library DevOps: All Features Complete

**Goal: Complete HeroParser library with all 4 major features**

- [ ] **T[Final+1]** [P] Integrate all 4 features into comprehensive library
- [ ] **T[Final+2]** [P] Final performance validation across all features
- [ ] **T[Final+3]** [P] Complete NuGet package publishing with full feature set
- [ ] **T[Final+4]** [P] Comprehensive documentation and samples for all features
- [ ] **T[Final+5]** **HEROPARSER COMPLETE**: World's fastest C# CSV/Fixed-Length parser ready

## Dependencies

### Feature-Based Development Flow:
1. **Feature 1: CSV Reading** → Setup → Cycles → DevOps → **FEATURE COMPLETE**
2. **Feature 2: Fixed-Length Reading** → Setup → Cycles → DevOps → **FEATURE COMPLETE**
3. **Feature 3: CSV Writing** → Setup → Cycles → DevOps → **FEATURE COMPLETE**
4. **Feature 4: Fixed-Length Writing** → Setup → Cycles → DevOps → **FEATURE COMPLETE**
5. **Final Integration** → All features combined → **LIBRARY COMPLETE**

### Feature 1 (CSV Reading) Dependencies:
- T007 (CsvConfiguration) blocks T009 (CsvParserBuilder)
- T008 (CsvParser) blocks T010 (ICsvParser), T016 (enhanced CsvParser)
- **CYCLE GATE**: Each cycle's Implement → Test → Benchmark must complete before decision
- **USER DECISION**: Only you decide when Feature 1 is complete and ready to move to Feature 2

### Feature 1 Decision-Driven Flow:
- **GATE 1**: Setup complete (T005) before F1 Cycle 1
- **DECISION 1**: After F1 Cycle 1 benchmark (T015) → Continue F1 Cycle 2 OR Feature 1 DevOps?
- **DECISION 2**: After F1 Cycle 2 benchmark (T025) → Continue F1 Cycle 3 OR Feature 1 DevOps?
- **DECISION 3**: After F1 Cycle 3 benchmark (T032) → Continue F1 Cycle N OR Feature 1 DevOps?
- **FEATURE DECISION**: After Feature 1 DevOps (T045) → Start Feature 2 (new branch) OR satisfied with CSV reading only?
- **FLEXIBILITY**: Can add more CSV reading cycles or move to next feature at any decision point

## Parallel Execution Examples

### Cycle 1 - Implementation Phase:
```bash
# Independent classes can be implemented in parallel
Task: "Create exception classes in src/Exceptions/"
Task: "Create basic CsvConfiguration in src/Configuration/"
# Then sequential: CsvParser → CsvParserBuilder → ICsvParser (dependencies)
```

### Cycle 1 - Test Phase:
```bash
# All test files can be created in parallel (different files)
Task: "Create basic CSV parsing tests in tests/Core/"
Task: "Create CsvParserBuilder tests in tests/Configuration/"
Task: "Create basic RFC 4180 compliance tests in tests/Compliance/"
```

### Cycle 2 - Implementation Phase:
```bash
# Memory optimizations can be implemented in parallel (different files)
Task: "Implement ArrayPool<T> memory management in src/Memory/"
Task: "Implement Span<char> string operations in src/Core/"
# Then sequential: Refactor CsvParser with optimizations
```

### Cycle 2 - Test Phase:
```bash
# Test suites can be created in parallel (different files)
Task: "Create contract tests from contracts/csv-parser-api.md"
Task: "Create memory allocation tests in tests/Performance/"
Task: "Expand RFC 4180 compliance tests for enhanced features"
```

### Cycle 3 - Implementation Phase:
```bash
# Fixed-length components can be implemented in parallel (different files)
Task: "Create FieldLayout and FieldDefinition in src/FixedLength/"
Task: "Create CobolCopybook entities in src/FixedLength/"
# Then sequential: SIMD optimization → FixedLengthParser implementation
```

### Cycle 4 - Implementation Phase:
```bash
# Advanced features can be implemented in parallel (different files)
Task: "Implement source generator for allocation-free mapping"
Task: "Implement PipeReader/Writer integration in src/Streaming/"
# Advanced async patterns build on existing foundation
```

### DevOps Phase - Final Automation:
```bash
# All DevOps tasks can run in parallel (different systems)
Task: "Setup CI/CD pipeline with automated testing and benchmarking"
Task: "Configure performance regression detection"
Task: "Setup multi-platform testing"
Task: "Create comprehensive benchmark comparison report"
Task: "Update documentation with final performance results"
```

## Constitutional Compliance Checkpoints

### After F1 Cycle 1 (T015 - Basic CSV Reading Foundation):
- [ ] Basic CSV reading functionality operational in NuGet library structure
- [ ] Initial CSV reading benchmark baseline established vs Sep, Sylvan.Data.Csv, CsvHelper
- [ ] All CSV reading tests passing for basic implementation across test projects
- [ ] NuGet package builds successfully across all 7 target frameworks
- [ ] **DECISION CRITERIA**: Is CSV reading performance acceptable OR continue optimizing?

### After F1 Cycle 2 (T025 - CSV Reading Memory Optimization):
- [ ] CSV reading memory optimizations implemented (ArrayPool<T>, Span<char>)
- [ ] CSV reading contract tests established for full API surface
- [ ] Memory allocation profiling shows improvements for CSV reading
- [ ] Benchmark comparison vs F1 Cycle 1 shows measurable CSV reading gains
- [ ] **DECISION CRITERIA**: Are CSV reading memory optimizations sufficient OR need SIMD?

### After F1 Cycle 3 (T032 - CSV Reading SIMD Optimization):
- [ ] SIMD intrinsics and unsafe code implemented for CSV reading
- [ ] CSV reading constitutional performance targets validated (>25 GB/s single-threaded)
- [ ] Platform-specific SIMD optimizations verified for CSV reading
- [ ] **DECISION CRITERIA**: CSV reading constitutional targets met → Feature 1 DevOps OR want advanced CSV reading features?

### After F1 Cycle N (T039 - Advanced CSV Reading Features):
- [ ] Advanced CSV reading features operational (source generators, streaming)
- [ ] Zero allocations for 99th percentile CSV reading verified
- [ ] >20% performance advantage over Sep demonstrated for CSV reading
- [ ] **DECISION CRITERIA**: CSV reading feature complete → Feature 1 DevOps OR discover new CSV reading optimizations?

### After Feature 1 DevOps (T045 - CSV Reading Production Ready):
- [ ] CI/CD pipeline operational for CSV reading with automated testing and benchmarking
- [ ] Performance regression detection active (<2% tolerance) for CSV reading
- [ ] CSV reading benchmark comparison report complete vs Sep, Sylvan.Data.Csv, CsvHelper
- [ ] CSV reading documentation complete with API docs, performance results
- [ ] Sample project demonstrating CSV reading usage and performance complete
- [ ] **FEATURE 1 COMPLETE**: CSV Reading ready for production use

### After Feature 2 (Fixed-Length Reading - Future):
- [ ] Fixed-length reading with COBOL, IBM, NACHA support complete
- [ ] Performance targets met for fixed-length reading
- [ ] **FEATURE 2 COMPLETE**: Ready for Feature 3 (CSV Writing)

### After Feature 3 (CSV Writing - Future):
- [ ] High-performance CSV writing with same optimizations as reading
- [ ] Performance targets met for CSV writing
- [ ] **FEATURE 3 COMPLETE**: Ready for Feature 4 (Fixed-Length Writing)

### After Feature 4 (Fixed-Length Writing - Future):
- [ ] Complete fixed-length writing support
- [ ] All 4 major features integrated and tested
- [ ] **FEATURE 4 COMPLETE**: Ready for final library integration

### After Final Library Integration:
- [ ] All 4 features working together seamlessly
- [ ] Comprehensive NuGet package with all reading/writing capabilities
- [ ] Complete performance validation across all features
- [ ] Full documentation and samples for entire library
- [ ] **HEROPARSER COMPLETE**: World's fastest C# CSV/Fixed-Length parser library ready

## Validation Checklist
*GATE: All must pass before task execution*

- [x] All contracts have corresponding tests (T010, T011)
- [x] All entities have model tasks (T014-T016, T020)
- [x] All tests come before implementation (T006-T013 before T014-T022)
- [x] Parallel tasks truly independent (different files, no shared dependencies)
- [x] Each task specifies exact file path
- [x] No task modifies same file as another [P] task
- [x] Constitutional requirements embedded in task descriptions
- [x] Feature-based ordering enforced (Feature 1: CSV Reading → Feature 2: Fixed-Length Reading → Feature 3: CSV Writing → Feature 4: Fixed-Length Writing)

## Notes
- **[P] tasks** = different files, no dependencies - safe for parallel execution
- **Feature-focused cycles**: Implement → Test → Benchmark → **USER DECISION** pattern for each major feature
- **4 Major Features**: CSV Reading → Fixed-Length Reading → CSV Writing → Fixed-Length Writing
- **Feature completion**: You decide when each feature is ready for production before moving to next
- **Benchmarking every cycle**: Immediate performance feedback guides optimization decisions
- **Constitutional gates**: Performance regression >2% blocks progress
- **Multi-framework targeting**: All tasks must validate across 7 target frameworks
- **Feature DevOps**: Each feature gets production-ready pipeline before moving to next
- **Fast iterations**: Each cycle should complete in days for rapid feature development
- **New branches**: Each feature can be developed in separate branches with clean integration