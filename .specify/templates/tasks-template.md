# Tasks: [FEATURE NAME]

**Input**: Design documents from `/specs/[###-feature-name]/`
**Prerequisites**: plan.md (required), research.md, data-model.md, contracts/

## Execution Flow (main)
```
1. Load plan.md from feature directory
   → If not found: ERROR "No implementation plan found"
   → Extract: tech stack, libraries, structure
2. Load optional design documents:
   → data-model.md: Extract entities → model tasks
   → contracts/: Each file → contract test task
   → research.md: Extract decisions → setup tasks
3. Generate tasks by category:
   → Setup: project init, dependencies, linting
   → Tests: contract tests, integration tests
   → Core: models, services, CLI commands
   → Integration: DB, middleware, logging
   → Polish: unit tests, performance, docs
4. Apply task rules:
   → Different files = mark [P] for parallel
   → Same file = sequential (no [P])
   → Tests before implementation (TDD)
5. Number tasks sequentially (T001, T002...)
6. Generate dependency graph
7. Create parallel execution examples
8. Validate task completeness:
   → All contracts have tests?
   → All entities have models?
   → All endpoints implemented?
9. Return: SUCCESS (tasks ready for execution)
```

## Format: `[ID] [P?] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- Include exact file paths in descriptions

## Path Conventions
- **Single project**: `src/`, `tests/` at repository root
- **Web app**: `backend/src/`, `frontend/src/`
- **Mobile**: `api/src/`, `ios/src/` or `android/src/`
- Paths shown below assume single project - adjust based on plan.md structure

## Phase 3.1: Setup
- [ ] T001 Create project structure per implementation plan
- [ ] T002 Initialize [language] project with [framework] dependencies
- [ ] T003 [P] Configure linting and formatting tools

## Phase 3.2: Benchmarks & Tests First ⚠️ MUST COMPLETE BEFORE 3.3
**CRITICAL: Benchmarks MUST show >20% performance advantage over competitors**
**Constitution Requirements: Zero allocations for 99th percentile, RFC 4180 compliance**
- [ ] T004 [P] BenchmarkDotNet baseline vs Sep (21 GB/s), Sylvan.Data.Csv, CsvHelper
- [ ] T005 [P] Memory allocation profiling setup
- [ ] T006 [P] RFC 4180 compliance test suite
- [ ] T007 [P] Fixed-length format test suite (COBOL, NACHA)
- [ ] T008 [P] Performance tests: 1KB, 1MB, 1GB, 100GB files
- [ ] T009 [P] Zero-allocation verification tests

## Phase 3.3: Core Implementation (ONLY after benchmarks established)
**Constitution Requirements: SIMD intrinsics, unsafe contexts, zero allocations**
- [ ] T010 [P] Core parser with unsafe/SIMD optimizations
- [ ] T011 [P] ArrayPool<T> and memory pool implementation
- [ ] T012 [P] Span<char> based string operations
- [ ] T013 CSV reader with zero-allocation parsing
- [ ] T014 CSV writer with buffer pooling
- [ ] T015 Fixed-length parser implementation
- [ ] T016 Source generator for allocation-free mapping

## Phase 3.4: API & Integration
- [ ] T017 Synchronous and asynchronous API facades
- [ ] T018 Fluent configuration builder
- [ ] T019 Attribute-based mapping support
- [ ] T020 PipeReader/Writer integration

## Phase 3.5: Performance Validation & Polish
**Constitution Requirements: Must outperform competitors by >20%**
- [ ] T021 [P] Comprehensive benchmark suite execution
- [ ] T022 Performance regression detection setup
- [ ] T023 Memory allocation validation (zero for 99th percentile)
- [ ] T024 [P] Platform-specific SIMD optimization validation
- [ ] T025 [P] Update docs with RFC compliance matrix
- [ ] T026 Competitive benchmark comparison report

## Dependencies
- Benchmarks (T004-T009) before implementation (T010-T016)
- T010 blocks T013, T014, T015
- T016 blocks T019
- Core implementation before API layer (T017-T020)
- Implementation before validation (T021-T026)

## Parallel Example
```
# Launch T004-T009 together:
Task: "BenchmarkDotNet baseline vs Sep (21 GB/s), Sylvan.Data.Csv, CsvHelper"
Task: "Memory allocation profiling setup"
Task: "RFC 4180 compliance test suite"
Task: "Fixed-length format test suite (COBOL, NACHA)"
Task: "Performance tests: 1KB, 1MB, 1GB, 100GB files"
Task: "Zero-allocation verification tests"
```

## Notes
- [P] tasks = different files, no dependencies
- Establish benchmarks before implementing
- Performance regression >2% blocks merge
- Commit with benchmark results

## Task Generation Rules
*Applied during main() execution*

1. **From Contracts**:
   - Each contract file → contract test task [P]
   - Each endpoint → implementation task
   
2. **From Data Model**:
   - Each entity → model creation task [P]
   - Relationships → service layer tasks
   
3. **From User Stories**:
   - Each story → integration test [P]
   - Quickstart scenarios → validation tasks

4. **Ordering**:
   - Setup → Tests → Models → Services → Endpoints → Polish
   - Dependencies block parallel execution

## Validation Checklist
*GATE: Checked by main() before returning*

- [ ] All contracts have corresponding tests
- [ ] All entities have model tasks
- [ ] All tests come before implementation
- [ ] Parallel tasks truly independent
- [ ] Each task specifies exact file path
- [ ] No task modifies same file as another [P] task