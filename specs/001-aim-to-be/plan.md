
# Implementation Plan: HeroParser - World's Fastest C# CSV/Fixed-Length Parser

**Branch**: `001-aim-to-be` | **Date**: 2025-01-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-aim-to-be/spec.md`

## Execution Flow (/plan command scope)
```
1. Load feature spec from Input path
   → If not found: ERROR "No feature spec at {path}"
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   → Detect Project Type from context (web=frontend+backend, mobile=app+api)
   → Set Structure Decision based on project type
3. Fill the Constitution Check section based on the content of the constitution document.
4. Evaluate Constitution Check section below
   → If violations exist: Document in Complexity Tracking
   → If no justification possible: ERROR "Simplify approach first"
   → Update Progress Tracking: Initial Constitution Check
5. Execute Phase 0 → research.md
   → If NEEDS CLARIFICATION remain: ERROR "Resolve unknowns"
6. Execute Phase 1 → contracts, data-model.md, quickstart.md, agent-specific template file (e.g., `CLAUDE.md` for Claude Code, `.github/copilot-instructions.md` for GitHub Copilot, `GEMINI.md` for Gemini CLI, `QWEN.md` for Qwen Code or `AGENTS.md` for opencode).
7. Re-evaluate Constitution Check section
   → If new violations: Refactor design, return to Phase 1
   → Update Progress Tracking: Post-Design Constitution Check
8. Plan Phase 2 → Describe task generation approach (DO NOT create tasks.md)
9. STOP - Ready for /tasks command
```

**IMPORTANT**: The /plan command STOPS at step 7. Phases 2-4 are executed by other commands:
- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary
Build the world's fastest C# CSV/fixed-length parser targeting >25 GB/s single-threaded throughput (vs current leader Sep at 21 GB/s). Focus on zero-allocation parsing with SIMD optimization, multi-framework support, and RFC 4180 compliance. Primary technical approach: unsafe code with hardware acceleration, benchmark-driven development, and performance-first architecture.

## Technical Context
**Language/Version**: C# with multi-framework targeting (netstandard2.0, netstandard2.1, net6.0, net7.0, net8.0, net9.0, net10.0)
**Primary Dependencies**: BenchmarkDotNet (performance validation), Source Generators (allocation-free mapping), Zero external dependencies for core library
**Storage**: File system I/O with stream support, PipeReader/Writer for zero-copy scenarios
**Testing**: xUnit with comprehensive RFC 4180 compliance suite, property-based testing, fuzzing, integration tests >1GB files
**Target Platform**: Cross-platform (Windows, Linux, macOS) with hardware optimization (x64/ARM64, SIMD: AVX-512, AVX2, ARM NEON)
**Project Type**: Single high-performance library with NuGet package distribution
**Performance Goals**: >25 GB/s single-threaded, >50 GB/s multi-threaded parsing, <1ms startup time, zero allocations 99th percentile
**Constraints**: <1KB memory overhead per 1MB parsed, performance regression tolerance <2%, mandatory unsafe code for hot paths
**Scale/Scope**: Handle 100GB+ files, compete with Sep/Sylvan/CsvHelper, comprehensive API surface (simple + advanced)

## Constitution Check
*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**I. Continuous Build & Test Verification (CRITICAL)**:
- [x] Build verification planned across ALL target frameworks (constitutional mandate for all 7 frameworks)
- [x] Test verification enforced at each phase gate (no progression without passing tests)
- [x] Zero tolerance for compilation errors/regressions (constitutional requirement)
- [x] Mandatory verification sequence defined (dotnet clean, build, test cycle)

**II. Codebase-First Development (CRITICAL)**:
- [x] Codebase analysis required before new file creation (constitutional mandate)
- [x] Reuse priority over new file creation (prefer extending existing code)
- [x] Strong rationale documentation required for new files (constitutional requirement)
- [x] Pattern consistency enforcement planned (follow existing architectural patterns)

**III. Performance-First Architecture**:
- [x] Performance prioritized over convenience (constitutional requirement, feature spec explicitly targets performance leadership)
- [x] SIMD/unsafe contexts identified for hot paths (spec requires SIMD intrinsics, unsafe code mandatory)
- [x] Virtual dispatch and boxing avoided (constitutional mandate for hot paths)
- [x] Method inlining strategy defined (AggressiveInlining required by constitution)

**IV. Benchmark-Driven Development**:
- [x] BenchmarkDotNet baselines established (planned against Sep, Sylvan.Data.Csv, CsvHelper)
- [x] Comparison against Sep (21 GB/s), Sylvan.Data.Csv, CsvHelper planned (explicit requirement)
- [x] Multi-threading with workstation/server GC configured (feature spec includes this)
- [x] Performance regression threshold set (2%) (constitutional requirement)

**V. RFC 4180 Strict Compliance**:
- [x] RFC 4180 compliance verified (explicit feature requirement with 100% test pass rate)
- [x] Fixed-length format support planned (COBOL copybook, IBM formats, NACHA)
- [x] Format variations documented (Excel quirks, TSV via opt-in flags)
- [x] Compliance test suite defined (100% RFC 4180 compliance testing)

**VI. API Excellence & Consistency**:
- [x] Sync/async APIs designed (feature spec requires identical semantics)
- [x] Span<T>/Memory<T> support planned (zero-copy scenarios explicitly required)
- [x] Fluent configuration approach defined (advanced API through builders)
- [x] Breaking change strategy documented (major version bumps with migration tooling)

**VII. Zero-Allocation Mandate**:
- [x] ArrayPool<T> usage planned (constitutional requirement for memory pools)
- [x] Span<char> for string operations (constitutional mandate for string operations)
- [x] Source generators considered (allocation-free object mapping required)
- [x] GC collection targets set (0 for 99th percentile) (constitutional requirement)

**VIII. World-Class Performance Targets**:
- [x] Parse throughput target: >25 GB/s single-threaded (exceed Sep's 21 GB/s) (explicit target)
- [x] Write throughput target: >20 GB/s single-threaded (constitutional requirement)
- [x] Multi-threaded: >50 GB/s parse, >40 GB/s write (constitutional targets)
- [x] Must outperform Sep, Sylvan.Data.Csv, CsvHelper by >20% (constitutional requirement)

**Quality Assurance (Constitutional Requirements)**:
- [x] Benchmark suite on Windows, Linux, macOS (x64/ARM64) (constitutional mandate)
- [x] Detailed metrics: throughput, allocations, CPU cycles, cache misses (constitutional requirement)
- [x] Performance gates: CI/CD fails on >2% regression (constitutional mandate)
- [x] Platform-specific SIMD tests (constitutional requirement for hardware optimization)
- [x] Property-based testing for parser/writer round-trips (constitutional standard)
- [x] Fuzzing for malformed input handling (constitutional requirement)
- [x] Integration tests with >1GB files (constitutional testing standard)

**Technical Decision Framework (Constitutional Compliance)**:
- [x] Performance Trade-off Matrix defined: Correctness > Performance > Memory > API > Maintainability
- [x] Implementation Prioritization: P0 (parsing hot paths) > P1 (memory management) > P2 (API surface)
- [x] Experimental Development Protocol: Encourage experiments with >20% performance proof requirement

## Project Structure

### Documentation (this feature)
```
specs/[###-feature]/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)
```
# C# NuGet Library Structure (SELECTED for HeroParser)
src/
├── HeroParser/              # Main library project
│   ├── Core/               # Core parsing logic
│   ├── Configuration/      # Configuration classes
│   ├── FixedLength/       # Fixed-length parsing
│   ├── Memory/            # Memory management
│   ├── Streaming/         # Stream processing
│   ├── SourceGenerators/  # Source generator project
│   └── Exceptions/        # Exception classes
├── HeroParser.Benchmarks/  # Benchmark project
└── HeroParser.Sample/      # Sample/demo project

tests/
├── HeroParser.Tests/       # Unit tests
├── HeroParser.IntegrationTests/  # Integration tests
├── HeroParser.PerformanceTests/  # Performance tests
└── HeroParser.ComplianceTests/   # RFC compliance tests

# Root files
├── HeroParser.sln          # Solution file
├── Directory.Build.props   # Common MSBuild properties
├── nuget.config           # NuGet configuration
├── README.md              # Library documentation
└── CHANGELOG.md           # Version history
```

**Structure Decision**: C# NuGet Library with multi-targeting support (netstandard2.0-net10.0)

## Phase 0: Outline & Research
1. **Extract unknowns from Technical Context** above:
   - For each NEEDS CLARIFICATION → research task
   - For each dependency → best practices task
   - For each integration → patterns task

2. **Generate and dispatch research agents**:
   ```
   For each unknown in Technical Context:
     Task: "Research {unknown} for {feature context}"
   For each technology choice:
     Task: "Find best practices for {tech} in {domain}"
   ```

3. **Consolidate findings** in `research.md` using format:
   - Decision: [what was chosen]
   - Rationale: [why chosen]
   - Alternatives considered: [what else evaluated]

**Output**: research.md with all NEEDS CLARIFICATION resolved

## Phase 1: Design & Contracts
*Prerequisites: research.md complete*

1. **Extract entities from feature spec** → `data-model.md`:
   - Entity name, fields, relationships
   - Validation rules from requirements
   - State transitions if applicable

2. **Generate API contracts** from functional requirements:
   - For each user action → endpoint
   - Use standard REST/GraphQL patterns
   - Output OpenAPI/GraphQL schema to `/contracts/`

3. **Generate contract tests** from contracts:
   - One test file per endpoint
   - Assert request/response schemas
   - Tests must fail (no implementation yet)

4. **Extract test scenarios** from user stories:
   - Each story → integration test scenario
   - Quickstart test = story validation steps

5. **Update agent file incrementally** (O(1) operation):
   - Run `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude`
     **IMPORTANT**: Execute it exactly as specified above. Do not add or remove any arguments.
   - If exists: Add only NEW tech from current plan
   - Preserve manual additions between markers
   - Update recent changes (keep last 3)
   - Keep under 150 lines for token efficiency
   - Output to repository root

**Output**: data-model.md, /contracts/*, failing tests, quickstart.md, agent-specific file

## Phase 2: Task Planning Approach
*This section describes what the /tasks command will do - DO NOT execute during /plan*

**Task Generation Strategy**:
- Load `.specify/templates/tasks-template.md` as base
- Generate tasks from Phase 1 design docs (contracts, data model, quickstart)
- Each contract → contract test task [P]
- Each entity → model creation task [P] 
- Each user story → integration test task
- Implementation tasks to make tests pass

**Ordering Strategy**:
- TDD order: Tests before implementation 
- Dependency order: Models before services before UI
- Mark [P] for parallel execution (independent files)

**Estimated Output**: 25-30 numbered, ordered tasks in tasks.md

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation
*These phases are beyond the scope of the /plan command*

**Phase 3**: Task execution (/tasks command creates tasks.md)  
**Phase 4**: Implementation (execute tasks.md following constitutional principles)  
**Phase 5**: Validation (run tests, execute quickstart.md, performance validation)

## Complexity Tracking
*Fill ONLY if Constitution Check has violations that must be justified*

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |


## Progress Tracking
*This checklist is updated during execution flow*

**Phase Status**:
- [x] Phase 0: Research complete (/plan command)
- [x] Phase 1: Design complete (/plan command)
- [ ] Phase 2: Task planning complete (/plan command - describe approach only)
- [ ] Phase 3: Tasks generated (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:
- [x] Initial Constitution Check: PASS
- [x] Post-Design Constitution Check: PASS (all constitutional principles verified)
- [x] All NEEDS CLARIFICATION resolved
- [x] Complexity deviations documented (none - design fully compliant)

---
*Based on Constitution v2.3.0 - See `.specify/memory/constitution.md`*
