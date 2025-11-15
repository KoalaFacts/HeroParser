# HeroParser Test Suite

## Overview

Comprehensive test suite with **200+ test cases** ensuring correctness before benchmarking.

## Test Categories

### 1. Basic Correctness Tests (8 tests)
**File:** `BasicCorrectnessTests.cs`

Tests fundamental CSV parsing functionality:
- ✅ Simple CSV parsing (3x3 grid)
- ✅ Empty fields handling
- ✅ Single column CSV
- ✅ Many columns (100+)
- ✅ Long lines (1000+ chars)
- ✅ Different delimiters (tab, semicolon, pipe)
- ✅ CR/LF/CRLF line endings
- ✅ Type parsing (int, double, bool, DateTime, Guid)

### 2. Edge Case Tests (20 tests)
**File:** `EdgeCaseTests.cs`

Tests boundary conditions and special scenarios:
- ✅ Empty CSV
- ✅ Single row without newline
- ✅ Trailing/leading delimiters
- ✅ Only delimiters (empty columns)
- ✅ Very long lines (>1000 chars)
- ✅ Very long fields (>1000 chars)
- ✅ Mixed line endings (CR/LF/CRLF in same file)
- ✅ Unicode characters (Japanese, German, French)
- ✅ Special characters (!@#$%^&*())
- ✅ Various column counts (10, 32, 64, 65, 100, 500)
- ✅ Different delimiters (semicolon, pipe, tab)

### 3. Integration Tests (25 tests)
**File:** `IntegrationTests.cs`

Tests complete API surface:
- ✅ `Csv.Parse()` with span
- ✅ `Csv.ParseComma()` specialized
- ✅ `Csv.ParseTab()` specialized
- ✅ `CsvReader.MoveNext()` iteration
- ✅ `CsvRow` indexing and count
- ✅ `CsvRow.ToStringArray()` materialization
- ✅ `CsvCol.Span` raw access
- ✅ `CsvCol.Length` and `IsEmpty`
- ✅ `CsvCol.Parse<T>()` generic parsing
- ✅ `CsvCol.TryParse<T>()` safe parsing
- ✅ Type-specific parsing methods
- ✅ `CsvCol.Equals()` comparisons
- ✅ Implicit conversions
- ✅ Large datasets (1000 rows, 100 columns)
- ✅ Real-world CSV examples

### 4. SIMD Validation Tests (100+ tests)
**File:** `SimdValidationTests.cs`

Ensures SIMD implementations match scalar results:
- ✅ Simple lines (various lengths)
- ✅ Exact chunk boundaries (32, 64 chars)
- ✅ Over chunk boundaries (33, 65, 100, 500, 1000 chars)
- ✅ Various column counts (1, 5, 16, 31, 32, 33, 63, 64, 65, 100, 200)
- ✅ Empty fields in different positions
- ✅ Long fields (100+ chars)
- ✅ Different delimiters (comma, semicolon, tab, pipe, colon)
- ✅ Unicode content
- ✅ Special characters
- ✅ Delimiters at exact chunk boundaries
- ✅ Multiple delimiters in single chunk
- ✅ No delimiters in chunk (fast-path test)
- ✅ **100 random CSV lines** (fuzz testing)

### 5. Parser Unit Tests (10 tests)
**File:** `ParserTests.cs`

Direct tests of parser implementations:
- ✅ ScalarParser basic functionality
- ✅ ScalarParser empty fields
- ✅ ScalarParser no delimiters
- ✅ ScalarParser empty line
- ✅ SimdParserFactory returns parser
- ✅ SimdParserFactory hardware info
- ✅ Avx512Parser availability check
- ✅ Avx2Parser availability check
- ✅ NeonParser availability check

### 6. SIMD Correctness Tests (5 tests)
**File:** `SimdCorrectnessTests.cs`

Cross-validation between parsers:
- ✅ AVX-512 matches Scalar
- ✅ AVX2 matches Scalar
- ✅ Long lines (>64 chars) all parsers match

## Test Statistics

**Total Test Methods:** ~170+
**Total Test Cases:** 200+ (including parameterized tests)
**Code Coverage Target:** >95% of core parsing logic

## Running Tests

### Quick Test Run
```bash
dotnet test tests/HeroParser.Tests/
```

### Verbose Output
```bash
dotnet test tests/HeroParser.Tests/ -v normal
```

### Run Specific Test Class
```bash
dotnet test tests/HeroParser.Tests/ --filter BasicCorrectnessTests
dotnet test tests/HeroParser.Tests/ --filter SimdValidationTests
```

### Run Single Test
```bash
dotnet test tests/HeroParser.Tests/ --filter "SimpleCsv_ParsesCorrectly"
```

### With Coverage (if tool installed)
```bash
dotnet test tests/HeroParser.Tests/ /p:CollectCoverage=true
```

## Expected Test Execution Time

- **Scalar-only tests:** ~1-2 seconds
- **Full SIMD validation:** ~3-5 seconds
- **Total suite:** ~5-10 seconds

## Test Success Criteria

All tests **must pass** before running benchmarks:

✅ **Zero failures**
✅ **Zero skipped** (unless hardware limitation)
✅ **100% SIMD validation** (all SIMD parsers match scalar)
✅ **Edge cases handled** (empty, large, unicode, etc.)

## Hardware-Dependent Tests

Some tests are skipped if hardware doesn't support SIMD:

- **AVX-512 tests**: Require `Avx512F.IsSupported && Avx512BW.IsSupported`
- **AVX2 tests**: Require `Avx2.IsSupported`
- **NEON tests**: Require `AdvSimd.IsSupported` (ARM only)

This is **normal** - tests will pass on available hardware.

## Key Validation Tests

### 1. Chunk Boundary Tests
Ensures SIMD correctly handles data at:
- 31, 32, 33 characters (AVX2 boundary)
- 63, 64, 65 characters (AVX-512 boundary)

### 2. Random Fuzz Testing
Generates 100 random CSV lines with:
- Random field lengths (0-20 chars)
- Random delimiter placement
- Validates all parsers produce identical results

### 3. Real-World Examples
Tests actual CSV patterns:
```csv
Name,Age,City,Salary
John Doe,30,New York,75000
Jane Smith,25,San Francisco,85000
```

## Test-Driven Development Flow

1. **Write Test** → Define expected behavior
2. **Run Test** → Verify it fails (red)
3. **Implement** → Write minimal code to pass
4. **Run Test** → Verify it passes (green)
5. **Refactor** → Optimize while tests stay green

## Debugging Failed Tests

If tests fail:

1. **Check which parser failed:**
   - Error message shows: "AVX-512 parser..." or "AVX2 parser..."

2. **Check the input:**
   - Error message includes: `for line: 'actual csv data'`

3. **Check the mismatch:**
   - Column count mismatch?
   - Column start position mismatch?
   - Column length mismatch?

4. **Run in debugger:**
   ```bash
   # Set breakpoint in test, then:
   dotnet test tests/HeroParser.Tests/ --filter "FailingTestName"
   ```

## Coverage Areas

### ✅ Covered
- CSV parsing (all formats)
- SIMD implementations (AVX-512, AVX2, NEON)
- Type parsing (int, double, DateTime, etc.)
- Edge cases (empty, large, unicode)
- API surface (Parse, ParseComma, ParseTab, etc.)
- Error cases (invalid parsing)

### ⏳ Not Yet Covered (Future)
- Quote handling (not implemented yet)
- Escape sequences (not implemented yet)
- Memory-mapped files (CsvFileReader - needs .NET 10)
- Parallel parsing (ParallelCsvReader - needs .NET 10)
- Performance regression tests

## Test Maintenance

### Adding New Tests

1. **Choose appropriate test file:**
   - Basic functionality → BasicCorrectnessTests.cs
   - Edge cases → EdgeCaseTests.cs
   - API integration → IntegrationTests.cs
   - SIMD validation → SimdValidationTests.cs

2. **Follow naming convention:**
   ```csharp
   [Fact]
   public void ComponentName_Scenario_ExpectedBehavior()
   {
       // Arrange
       var input = "test,data";

       // Act
       var result = Csv.Parse(input.AsSpan());

       // Assert
       Assert.Equal(expected, actual);
   }
   ```

3. **Add to this document** if new category

## Continuous Integration

For CI/CD pipelines:

```yaml
# Example GitHub Actions
- name: Run Tests
  run: dotnet test tests/HeroParser.Tests/ --logger "trx;LogFileName=test-results.trx"

- name: Check Test Results
  run: |
    if [ $? -ne 0 ]; then
      echo "Tests failed!"
      exit 1
    fi
```

## Next Steps After Tests Pass

Once all tests pass:

1. ✅ **Commit tests** to version control
2. ✅ **Run benchmarks** (QuickTest)
3. ✅ **Compare with Sep** (VsSepBenchmark)
4. ✅ **Optimize if needed** (profile hot paths)
5. ✅ **Re-run tests** after any optimization

---

**Status:** ✅ Test suite complete and ready for execution!

Run: `dotnet test tests/HeroParser.Tests/`
