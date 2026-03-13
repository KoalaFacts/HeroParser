# Column Validation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add inline source-generated field validation to both CSV and FixedWidth column attributes, preserving lazy iteration. Errors collected during enumeration, failed rows excluded.

**Architecture:** Validation properties on column attributes. Source generators emit inline validation checks + static Regex fields. `ICsvBinder` gains optional `errors` parameter. Readers gain `Errors` property. Lazy iteration preserved — no eager materialization.

**Tech Stack:** C# source generators (IIncrementalGenerator), Roslyn, xUnit.v3, multi-TFM (net8.0/net9.0/net10.0)

**Spec:** `docs/superpowers/specs/2026-03-11-column-validation-design.md`

---

## Chunk 1: Shared Types + Column Attribute Changes

### Task 1: Create ValidationError struct

**Files:**
- Create: `src/HeroParser/Validation/ValidationError.cs`
- Create: `tests/HeroParser.Tests/Validation/ValidationErrorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Validation;

public class ValidationErrorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ValidationError_CanBeCreatedWithAllProperties()
    {
        var error = new ValidationError
        {
            RowNumber = 5, ColumnIndex = 2, ColumnName = "Amount",
            PropertyName = "Amount", Rule = "NotNull",
            Message = "Value is required", RawValue = ""
        };
        Assert.Equal(5, error.RowNumber);
        Assert.Equal("NotNull", error.Rule);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidationError_ColumnNameCanBeNull()
    {
        var error = new ValidationError { ColumnName = null, PropertyName = "Id", Rule = "NotNull", Message = "fail" };
        Assert.Null(error.ColumnName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidationError_RawValueCanBeNull()
    {
        var error = new ValidationError { PropertyName = "Id", Rule = "NotNull", Message = "fail", RawValue = null };
        Assert.Null(error.RawValue);
    }
}
```

- [ ] **Step 2: Run test — expect FAIL** (`ValidationError` not found)

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~ValidationErrorTests" -v minimal`

- [ ] **Step 3: Implement ValidationError**

```csharp
namespace HeroParser.Validation;

public readonly struct ValidationError
{
    public int RowNumber { get; init; }
    public int ColumnIndex { get; init; }
    public string? ColumnName { get; init; }
    public string PropertyName { get; init; }
    public string Rule { get; init; }
    public string Message { get; init; }
    public string? RawValue { get; init; }
}
```

- [ ] **Step 4: Run test — expect PASS**
- [ ] **Step 5: Commit**

---

### Task 2: Add validation properties to CsvColumnAttribute

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Reading/Shared/CsvColumnAttribute.cs`

- [ ] **Step 1: Add validation properties**

Add to `CsvColumnAttribute`:
```csharp
public bool NotNull { get; init; }
public bool NotEmpty { get; init; }
public int MaxLength { get; init; } = -1;
public int MinLength { get; init; } = -1;
public double RangeMin { get; init; } = double.NaN;
public double RangeMax { get; init; } = double.NaN;
public string? Pattern { get; init; }
public int PatternTimeoutMs { get; init; } = 1000;
```

- [ ] **Step 2: Build — expect SUCCESS**

Run: `dotnet build src/HeroParser`

- [ ] **Step 3: Commit**

---

### Task 3: Add validation properties to FixedWidthColumnAttribute

**Files:**
- Modify: `src/HeroParser/FixedWidths/Records/Binding/FixedWidthColumnAttribute.cs`

- [ ] **Step 1: Add same validation properties as Task 2**
- [ ] **Step 2: Build — expect SUCCESS**
- [ ] **Step 3: Commit**

---

### Task 4: Update ICsvBinder interface with errors parameter

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Reading/Binders/ICsvBinder.cs`
- Modify: All `ICsvBinder` implementations (reflection-based, descriptor-based, adapter)

- [ ] **Step 1: Update interface**

```csharp
bool TryBind(CsvRow<TElement> row, int rowNumber, out T result, List<ValidationError>? errors = null);
bool BindInto(ref T instance, CsvRow<TElement> row, int rowNumber, List<ValidationError>? errors = null);
```

- [ ] **Step 2: Find and update all implementations**

Run: `grep -rn "ICsvBinder" src/HeroParser/` to find all implementations. Add the `errors` parameter to each. Non-generated implementations ignore it.

- [ ] **Step 3: Add Errors property to CsvRecordReader**

Add `public IReadOnlyList<ValidationError> Errors` property. Internally use `List<ValidationError>` that gets populated during iteration and passed to `TryBind`.

- [ ] **Step 4: Build — expect SUCCESS**

Run: `dotnet build src/HeroParser`

- [ ] **Step 5: Run existing tests — expect PASS** (errors parameter is optional/defaulted)

Run: `dotnet test -v minimal`

- [ ] **Step 6: Commit**

---

### Task 5: Add HERO008 diagnostic + fix existing CsvColumn usages

**Important:** HERO008 must be added alongside fixing all existing `[CsvColumn]` usages that lack Name/Index, to avoid breaking the build.

**Files:**
- Modify: `src/HeroParser.Generators/CsvRecordBinderGenerator.cs`
- Modify: All files using `[CsvColumn]` without explicit Name/Index

- [ ] **Step 1: Find all bare [CsvColumn] usages**

Run: `grep -rn "\[CsvColumn\]" tests/ benchmarks/ src/` and `grep -rn "\[CsvColumn(" tests/ benchmarks/ src/` — identify any that lack `Name =` or `Index =`.

- [ ] **Step 2: Update all bare usages to include explicit Name or Index**

- [ ] **Step 3: Add HERO008 diagnostic descriptor**

```csharp
private static readonly DiagnosticDescriptor missingNameOrIndexDiagnostic = new(
    "HERO008", "CsvColumn requires Name or Index",
    "Property '{0}' has [CsvColumn] but neither Name nor Index is specified.",
    "HeroParser.Generators", DiagnosticSeverity.Error, isEnabledByDefault: true);
```

- [ ] **Step 4: Add check in BuildDescriptor** — if `mapAttribute is not null` and neither Name nor Index were set, report HERO008 and skip member.

- [ ] **Step 5: Write test for HERO008**

```csharp
[Fact]
[Trait("Category", "Unit")]
public void Generator_WithoutNameOrIndex_ReportsHERO008()
{
    var source = """
        using HeroParser.SeparatedValues.Reading.Shared;
        namespace TestNamespace;
        [CsvGenerateBinder]
        public class Bad { [CsvColumn(NotNull = true)] public string Name { get; set; } = ""; }
        """;
    var result = RunGenerator(source);
    Assert.Contains(result.Diagnostics, d => d.Id == "HERO008");
}
```

- [ ] **Step 6: Run all tests — expect PASS**

Run: `dotnet test -v minimal`

- [ ] **Step 7: Commit**

---

### Task 6: Add HERO004-HERO007 diagnostics

**Files:**
- Modify: `src/HeroParser.Generators/CsvRecordBinderGenerator.cs`
- Modify: `tests/HeroParser.Generators.Tests/CsvRecordBinderGeneratorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
[Trait("Category", "Unit")]
public void Generator_NotEmptyOnInt_ReportsHERO004()
{
    var source = """
        using HeroParser.SeparatedValues.Reading.Shared;
        namespace T;
        [CsvGenerateBinder]
        public class R { [CsvColumn(Index = 0, NotEmpty = true)] public int X { get; set; } }
        """;
    Assert.Contains(RunGenerator(source).Diagnostics, d => d.Id == "HERO004");
}

[Fact]
[Trait("Category", "Unit")]
public void Generator_MaxLengthOnDecimal_ReportsHERO005()
{
    var source = """
        using HeroParser.SeparatedValues.Reading.Shared;
        namespace T;
        [CsvGenerateBinder]
        public class R { [CsvColumn(Index = 0, MaxLength = 10)] public decimal X { get; set; } }
        """;
    Assert.Contains(RunGenerator(source).Diagnostics, d => d.Id == "HERO005");
}

[Fact]
[Trait("Category", "Unit")]
public void Generator_RangeOnString_ReportsHERO006()
{
    var source = """
        using HeroParser.SeparatedValues.Reading.Shared;
        namespace T;
        [CsvGenerateBinder]
        public class R { [CsvColumn(Name = "X", RangeMin = 0)] public string X { get; set; } = ""; }
        """;
    Assert.Contains(RunGenerator(source).Diagnostics, d => d.Id == "HERO006");
}

[Fact]
[Trait("Category", "Unit")]
public void Generator_PatternOnInt_ReportsHERO007()
{
    var source = """
        using HeroParser.SeparatedValues.Reading.Shared;
        namespace T;
        [CsvGenerateBinder]
        public class R { [CsvColumn(Index = 0, Pattern = ".*")] public int X { get; set; } }
        """;
    Assert.Contains(RunGenerator(source).Diagnostics, d => d.Id == "HERO007");
}
```

- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Add diagnostic descriptors and validation logic in BuildDescriptor**
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

---

## Chunk 2: CSV Generator — Emit Inline Validation

### Task 7: Extract validation properties in CsvRecordBinderGenerator.BuildDescriptor

**Files:**
- Modify: `src/HeroParser.Generators/CsvRecordBinderGenerator.cs`

- [ ] **Step 1: Extend MemberDescriptor**

Add fields: `ValidationNotNull`, `ValidationNotEmpty`, `ValidationMaxLength`, `ValidationMinLength`, `ValidationRangeMin`, `ValidationRangeMax`, `ValidationPattern`, `ValidationPatternTimeoutMs`

- [ ] **Step 2: Read properties from attribute in BuildDescriptor**

In the `foreach (var arg in mapAttribute.NamedArguments)` loop, add cases for all validation properties.

- [ ] **Step 3: Build — expect SUCCESS**
- [ ] **Step 4: Commit**

---

### Task 8: Emit validation code in generated CSV binder

**Files:**
- Modify: `src/HeroParser.Generators/CsvRecordBinderGenerator.cs`
- Modify: `tests/HeroParser.Generators.Tests/CsvRecordBinderGeneratorTests.cs`

Key implementation details:
- `BindInto` gains `List<ValidationError>? errors` parameter
- Tracks `bool valid = true;`
- After each property parse, emits validation checks
- For `Pattern`: emit `private static readonly Regex _pattern_{PropertyName}` field with timeout
- For `Range`: emit comparisons in target type literal (e.g., `< 0m` for decimal, `< 0L` for long)
- Returns `valid` at end

- [ ] **Step 1: Write tests**

```csharp
[Fact]
[Trait("Category", "Unit")]
public void Generator_WithNotNullValidation_EmitsValidationCode()
{
    var source = """
        using HeroParser.SeparatedValues.Reading.Shared;
        namespace T;
        [CsvGenerateBinder]
        public class R { [CsvColumn(Name = "X", NotNull = true)] public string X { get; set; } = ""; }
        """;
    var code = string.Join("\n", RunGenerator(source).GeneratedSources.Select(s => s.SourceText.ToString()));
    Assert.Contains("ValidationError", code);
    Assert.Contains("NotNull", code);
}

[Fact]
[Trait("Category", "Unit")]
public void Generator_WithNoValidation_NoValidationCode()
{
    var source = """
        using HeroParser.SeparatedValues.Reading.Shared;
        namespace T;
        [CsvGenerateBinder]
        public class R { [CsvColumn(Name = "X")] public string X { get; set; } = ""; }
        """;
    var code = string.Join("\n", RunGenerator(source).GeneratedSources.Select(s => s.SourceText.ToString()));
    Assert.DoesNotContain("ValidationError", code);
}

[Fact]
[Trait("Category", "Unit")]
public void Generator_WithPattern_EmitsStaticRegex()
{
    var source = """
        using HeroParser.SeparatedValues.Reading.Shared;
        namespace T;
        [CsvGenerateBinder]
        public class R { [CsvColumn(Name = "X", Pattern = @"^\d+$")] public string? X { get; set; } }
        """;
    var code = string.Join("\n", RunGenerator(source).GeneratedSources.Select(s => s.SourceText.ToString()));
    Assert.Contains("static readonly", code);
    Assert.Contains("Regex", code);
    Assert.Contains("TimeSpan", code);
}

[Fact]
[Trait("Category", "Unit")]
public void Generator_WithDecimalRange_EmitsDecimalLiteral()
{
    var source = """
        using HeroParser.SeparatedValues.Reading.Shared;
        namespace T;
        [CsvGenerateBinder]
        public class R { [CsvColumn(Index = 0, RangeMin = 0, RangeMax = 999.99)] public decimal X { get; set; } }
        """;
    var code = string.Join("\n", RunGenerator(source).GeneratedSources.Select(s => s.SourceText.ToString()));
    Assert.Contains("999.99m", code);  // decimal literal
}
```

- [ ] **Step 2: Run — expect FAIL**
- [ ] **Step 3: Implement validation emission**

Key methods to add/modify:
- `EmitByteBindIntoMethod` — add `errors` parameter, `bool valid = true;`, return `valid`
- `EmitByteInlinePropertyBinding` — after parsing, call new `EmitValidationChecks`
- New `EmitValidationChecks(SourceBuilder, MemberDescriptor)` — emits checks per property
- New `EmitStaticRegexField(SourceBuilder, MemberDescriptor)` — for Pattern
- New helper `FormatRangeLiteral(double value, string baseType)` — returns "0m", "100L", etc.

- [ ] **Step 4: Run all generator tests — expect PASS**

Run: `dotnet test tests/HeroParser.Generators.Tests -v minimal`

- [ ] **Step 5: Commit**

---

## Chunk 3: CSV End-to-End Integration Tests

### Task 9: Create test record types with validation

**Files:**
- Create: `tests/HeroParser.Tests/Validation/TestRecords.cs`
- Create: `tests/HeroParser.Tests/Validation/CsvColumnValidationTests.cs`

- [ ] **Step 1: Create test record types**

```csharp
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.Tests.Validation;

[CsvGenerateBinder]
public class ValidatedTransaction
{
    [CsvColumn(Name = "Id", NotNull = true, NotEmpty = true)]
    public string TransactionId { get; set; } = "";

    [CsvColumn(Name = "Amount", Index = 1, NotNull = true, RangeMin = 0, RangeMax = 100000)]
    public decimal Amount { get; set; }

    [CsvColumn(Name = "Currency", Index = 2, NotNull = true, MinLength = 3, MaxLength = 3)]
    public string Currency { get; set; } = "";

    [CsvColumn(Name = "Reference", Index = 3, Pattern = @"^[A-Z]{2}\d{4}$")]
    public string? Reference { get; set; }
}
```

- [ ] **Step 2: Write integration tests**

Tests for: NotNull (missing value), NotEmpty (whitespace), MaxLength/MinLength, Range out of bounds, Pattern no match, valid data (no errors), multiple errors collected across rows.

- [ ] **Step 3: Run — expect PASS**
- [ ] **Step 4: Commit**

---

## Chunk 4: FixedWidth Generator + Validation

### Task 10: Extend FixedWidth MemberDescriptor with validation fields

Same pattern as Task 7 for `FixedWidthRecordBinderGenerator`.

- [ ] **Step 1: Add validation fields to FixedWidth MemberDescriptor**
- [ ] **Step 2: Read from attribute in BuildDescriptor**
- [ ] **Step 3: Build — expect SUCCESS**
- [ ] **Step 4: Commit**

---

### Task 11: Emit validation code in FixedWidth generated binder

Same pattern as Task 8 for FixedWidth. The setter methods gain validation checks.

- [ ] **Step 1: Write tests for FixedWidth validation code generation**
- [ ] **Step 2: Add errors parameter to FixedWidth binder interface/implementations**
- [ ] **Step 3: Add Errors property to FixedWidth reader**
- [ ] **Step 4: Implement validation emission in setter methods**
- [ ] **Step 5: Run all FixedWidth tests — expect PASS**
- [ ] **Step 6: Commit**

---

### Task 12: FixedWidth end-to-end integration tests

- [ ] **Step 1: Create test record types with FixedWidth validation**
- [ ] **Step 2: Write integration tests (same coverage as Task 9)**
- [ ] **Step 3: Run — expect PASS**
- [ ] **Step 4: Commit**

---

## Chunk 5: Remove Legacy FixedWidth Validation + Final Polish

### Task 13: Remove legacy FixedWidth validation attributes

**Files to delete:**
- `src/HeroParser/FixedWidths/Validation/FixedWidthValidationAttribute.cs`
- `src/HeroParser/FixedWidths/Validation/FixedWidthRequiredAttribute.cs`
- `src/HeroParser/FixedWidths/Validation/FixedWidthRangeAttribute.cs`
- `src/HeroParser/FixedWidths/Validation/FixedWidthRegexAttribute.cs`
- `src/HeroParser/FixedWidths/Validation/FixedWidthStringLengthAttribute.cs`

- [ ] **Step 1: Delete files**
- [ ] **Step 2: Fix any references to removed types**
- [ ] **Step 3: Run full test suite — expect PASS**
- [ ] **Step 4: Commit**

---

### Task 14: Add HERO004-007 diagnostics to FixedWidth generator

Same as Task 6 but for `FixedWidthRecordBinderGenerator`.

- [ ] **Step 1: Add diagnostic descriptors**
- [ ] **Step 2: Add validation logic in BuildDescriptor**
- [ ] **Step 3: Write tests**
- [ ] **Step 4: Run — expect PASS**
- [ ] **Step 5: Commit**

---

### Task 15: Update README

Document:
1. Validation properties on column attributes
2. Lazy error collection pattern
3. Breaking change: Name/Index required on CsvColumn
4. Example usage with validation
5. Compile-time diagnostics (HERO004-HERO008)
6. Pattern timeout configuration

- [ ] **Step 1: Update README**
- [ ] **Step 2: Commit**

---

### Task 16: Final verification

- [ ] **Step 1: Run full test suite**

Run: `dotnet test -v minimal`
Expected: ALL PASS

- [ ] **Step 2: Run benchmarks**

Run: `dotnet run -c Release --project benchmarks/HeroParser.Benchmarks -- --filter "*" --job short`
Expected: No significant regression

- [ ] **Step 3: Verify clean build**

Run: `dotnet build -warnaserror`
Expected: BUILD SUCCEEDED
