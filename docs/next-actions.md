# HeroParser Next Actions Plan

**Date**: 2025-01-22
**Current State**: Phase 3.5 in progress, compilation issues blocking progress
**Priority**: Fix compilation → Minimal viable parser → Performance validation

## 🔥 CRITICAL - Fix Compilation (Priority P0)

### Issues to Fix Immediately

1. **AVX-512 Intrinsics Reference** (`CpuOptimizations.cs:100`)
   ```csharp
   // Issue: Avx512VL doesn't exist
   // Fix: Use Avx512BW.IsSupported && Avx512VL.IsSupported
   // Or: Add proper using statement
   ```

2. **Ref Struct Scope Violation** (`CsvParser.cs:538`)
   ```csharp
   // Issue: Cannot use ref struct in async context
   // Fix: Modify enumerator to not leak ref struct scope
   ```

3. **ParseContext Accessibility** (`CsvParser.cs:473, 508`)
   ```csharp
   // Issue: internal class used in public struct
   // Fix: Make ParseContext public or restructure
   ```

4. **Unsafe Context Required** (`CsvParser.cs:172-173`)
   ```csharp
   // Issue: stackalloc without unsafe
   // Fix: Add unsafe context or use alternative
   ```

5. **CsvRecord Constructor Mismatch** (`CsvParser.cs:282`)
   ```csharp
   // Issue: fieldCount parameter doesn't exist
   // Fix: Check CsvRecord constructor signature
   ```

## ⚡ IMMEDIATE - Minimal Viable Parser (Priority P1)

### Goal: Get ONE working parser path
1. Fix compilation errors above
2. Implement `ScalarCsvParser<T>` with basic functionality
3. Create simple test: parse "a,b,c\n1,2,3" → verify fields
4. Run minimal benchmark to establish baseline

### Success Criteria:
- ✅ Compiles without errors
- ✅ Can parse simple CSV content
- ✅ Zero allocations for basic operations
- ✅ Baseline performance measurement

## 📈 SHORT TERM - Validation & Testing (Priority P2)

### Performance Baseline
1. Run against Sep library (21 GB/s baseline)
2. Verify zero-allocation guarantees
3. Test multi-framework compatibility
4. Measure memory overhead

### Basic Functionality
1. RFC 4180 compliance testing
2. Error handling verification
3. Configuration system testing
4. Integration with test data generators

## 🚀 MEDIUM TERM - Complete Phase 3.5

### T025: CSV Writer Implementation
- Buffer-pooled writing
- RFC 4180 compliant output
- Async streaming support

### T026: Fixed-Length Parser
- COBOL copybook support
- EBCDIC conversion
- Packed decimal handling

### T027: Type Mapping System
- Built-in type conversions
- Custom converter interface
- Source generator integration

## 🔧 TECHNICAL DEBT

### Code Quality
1. Add comprehensive XML documentation
2. Implement proper error messages
3. Add validation for edge cases
4. Performance regression tests

### Architecture
1. Complete SIMD implementations
2. Add more CPU-specific optimizations
3. Optimize buffer sizes based on profiling
4. Add streaming for very large files

## 📋 Quick Reference Commands

### Build & Test
```bash
# Build single framework to isolate issues
dotnet build src/HeroParser/HeroParser.csproj --framework net8.0

# Run specific test category
dotnet test tests/HeroParser.UnitTests --filter Category=Smoke

# Run benchmarks
dotnet run --project tests/HeroParser.BenchmarkTests -c Release
```

### Debug Compilation
```bash
# Verbose build output
dotnet build --verbosity diagnostic

# Framework-specific build
dotnet build --framework net8.0 --verbosity normal
```

## 🎯 Success Metrics

### Immediate (Fix Compilation)
- [ ] Zero compilation errors across all frameworks
- [ ] Can create basic CsvParser instance
- [ ] Can enumerate simple CSV content

### Short Term (Viable Parser)
- [ ] Parse 1MB CSV file without errors
- [ ] Zero allocations for 99% of operations
- [ ] Performance baseline established vs Sep

### Medium Term (Phase 3.5 Complete)
- [ ] All T024-T027 tasks implemented
- [ ] Beats Sep's 21 GB/s baseline
- [ ] RFC 4180 compliant
- [ ] Multi-framework compatibility verified

## 📞 Escalation

If blocked for >4 hours on any P0 issue:
1. Document the specific error
2. Create minimal reproduction case
3. Focus on alternative approach
4. Consider architectural changes if needed

## 🔗 Key Resources

- **Implementation Status**: `docs/implementation-status.md`
- **Task Details**: `specs/001-aim-to-be/tasks.md`
- **Architecture**: `specs/001-aim-to-be/research.md`
- **Constitution**: `specs/001-aim-to-be/plan.md`
- **Test Data**: `docs/test-data-generation.md`

---

**Next Session Goal**: Fix all compilation errors and achieve minimal working CSV parser