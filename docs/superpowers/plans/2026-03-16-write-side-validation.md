# Write-Side Validation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce `[Validate]` attribute rules (NotNull, NotEmpty, MaxLength, MinLength, Range, Pattern) during CSV and FixedWidth record writing, matching the read-side validation behavior.

**Architecture:** Add a `WriteValidationRunner` that validates property values after extraction but before serialization. The reflection-based writer (`CsvRecordWriter.WriteRecordInternal`) validates using runtime metadata from `[Validate]` attributes on `PropertyAccessor`. The source-generated writer path passes validation metadata through `WriterTemplate`. In `Strict` mode (default), the first validation error throws `ValidationException`. In `Lenient` mode, invalid records are skipped silently.

**Tech Stack:** C# with source generators, existing `ValidationError`/`ValidationException` infrastructure, `[Validate]` attribute metadata.

---

## File Structure

### New Files
- `src/HeroParser/Validation/WriteValidationRunner.cs` — Shared logic: validates an `object?` property value against `[Validate]` rules. Works with materialized values (not spans). Used by both CSV and FixedWidth reflection writers.
- `src/HeroParser/Validation/WritePropertyValidation.cs` — Holds per-property validation metadata for write-side (mirrors `CsvPropertyValidation` from read-side but for `object?` values).

### Modified Files
- `src/HeroParser/SeparatedValues/Writing/CsvWriteOptions.cs` — Add `ValidationMode` property (default `Strict`)
- `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs` — Read `[Validate]` in `BuildAccessors()`, store on `PropertyAccessor`, call `WriteValidationRunner` in `WriteRecordInternal()`
- `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs:WriterTemplate` — Add validation fields
- `src/HeroParser.Generators/CsvRecordBinderGenerator.cs:EmitWriterRegistration()` — Pass validation metadata to `WriterTemplate`
- `src/HeroParser/FixedWidths/Writing/FixedWidthWriteOptions.cs` — Add `ValidationMode` property (default `Strict`)
- `src/HeroParser/FixedWidths/Writing/FixedWidthRecordWriter.cs` — Same pattern: validate before writing

### Test Files
- `tests/HeroParser.Tests/Validation/CsvWriteValidationTests.cs` — New
- `tests/HeroParser.Tests/Validation/FixedWidthWriteValidationTests.cs` — New

---

## Chunk 1: Shared Validation Infrastructure

### Task 1: Create WritePropertyValidation metadata type

**Files:**
- Create: `src/HeroParser/Validation/WritePropertyValidation.cs`

- [ ] **Step 1: Create the validation metadata record**

```csharp
namespace HeroParser.Validation;

/// <summary>
/// Holds per-property validation rules for write-side enforcement.
/// </summary>
internal sealed record WritePropertyValidation(
    bool NotNull,
    bool NotEmpty,
    int? MaxLength,
    int? MinLength,
    double? RangeMin,
    double? RangeMax,
    string? Pattern,
    int PatternTimeoutMs = 1000)
{
    public bool HasAnyRule => NotNull || NotEmpty
        || MaxLength.HasValue || MinLength.HasValue
        || RangeMin.HasValue || RangeMax.HasValue
        || Pattern is not null;
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/HeroParser/Validation/WritePropertyValidation.cs
git commit -m "feat: Add WritePropertyValidation metadata type for write-side validation"
```

### Task 2: Create WriteValidationRunner

**Files:**
- Create: `src/HeroParser/Validation/WriteValidationRunner.cs`
- Test: `tests/HeroParser.Tests/Validation/WriteValidationRunnerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Validation;

[Trait("Category", "Unit")]
public class WriteValidationRunnerTests
{
    private static WritePropertyValidation NotNullRule => new(NotNull: true, NotEmpty: false, MaxLength: null, MinLength: null, RangeMin: null, RangeMax: null, Pattern: null);
    private static WritePropertyValidation NotEmptyRule => new(NotNull: false, NotEmpty: true, MaxLength: null, MinLength: null, RangeMin: null, RangeMax: null, Pattern: null);
    private static WritePropertyValidation MaxLengthRule(int max) => new(NotNull: false, NotEmpty: false, MaxLength: max, MinLength: null, RangeMin: null, RangeMax: null, Pattern: null);
    private static WritePropertyValidation RangeRule(double min, double max) => new(NotNull: false, NotEmpty: false, MaxLength: null, MinLength: null, RangeMin: min, RangeMax: max, Pattern: null);

    [Fact]
    public void NotNull_NullValue_ReturnsError()
    {
        var errors = new List<ValidationError>();
        var result = WriteValidationRunner.Validate(null, "Amount", 1, 0, NotNullRule, errors);
        Assert.True(result);
        Assert.Contains(errors, e => e.Rule == "NotNull");
    }

    [Fact]
    public void NotNull_EmptyString_ReturnsError()
    {
        var errors = new List<ValidationError>();
        var result = WriteValidationRunner.Validate("", "Amount", 1, 0, NotNullRule, errors);
        Assert.True(result);
        Assert.Contains(errors, e => e.Rule == "NotNull");
    }

    [Fact]
    public void NotNull_ValidValue_NoError()
    {
        var errors = new List<ValidationError>();
        var result = WriteValidationRunner.Validate(100m, "Amount", 1, 0, NotNullRule, errors);
        Assert.False(result);
        Assert.Empty(errors);
    }

    [Fact]
    public void NotNull_DefaultDecimal_NoError()
    {
        // 0m is a valid value, not null
        var errors = new List<ValidationError>();
        var result = WriteValidationRunner.Validate(0m, "Amount", 1, 0, NotNullRule, errors);
        Assert.False(result);
        Assert.Empty(errors);
    }

    [Fact]
    public void NotEmpty_WhitespaceString_ReturnsError()
    {
        var errors = new List<ValidationError>();
        var result = WriteValidationRunner.Validate("   ", "Name", 1, 0, NotEmptyRule, errors);
        Assert.True(result);
        Assert.Contains(errors, e => e.Rule == "NotEmpty");
    }

    [Fact]
    public void MaxLength_Exceeded_ReturnsError()
    {
        var errors = new List<ValidationError>();
        var result = WriteValidationRunner.Validate("ABCDEF", "Code", 1, 0, MaxLengthRule(3), errors);
        Assert.True(result);
        Assert.Contains(errors, e => e.Rule == "MaxLength");
    }

    [Fact]
    public void Range_OutOfBounds_ReturnsError()
    {
        var errors = new List<ValidationError>();
        var result = WriteValidationRunner.Validate(-5.0, "Amount", 1, 0, RangeRule(0, 100), errors);
        Assert.True(result);
        Assert.Contains(errors, e => e.Rule == "Range");
    }

    [Fact]
    public void NoRules_ReturnsNoError()
    {
        var noRules = new WritePropertyValidation(false, false, null, null, null, null, null);
        var errors = new List<ValidationError>();
        var result = WriteValidationRunner.Validate("anything", "Prop", 1, 0, noRules, errors);
        Assert.False(result);
        Assert.Empty(errors);
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test tests/HeroParser.Tests --filter WriteValidationRunnerTests -f net10.0`
Expected: Compilation error (WriteValidationRunner doesn't exist)

- [ ] **Step 3: Implement WriteValidationRunner**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace HeroParser.Validation;

/// <summary>
/// Validates materialized property values during record writing.
/// </summary>
internal static class WriteValidationRunner
{
    /// <summary>
    /// Validates a property value against write-side rules.
    /// Returns true if any validation error was found.
    /// </summary>
    public static bool Validate(
        object? value,
        string propertyName,
        int rowNumber,
        int columnIndex,
        WritePropertyValidation rules,
        List<ValidationError> errors)
    {
        if (!rules.HasAnyRule) return false;

        bool hasErrors = false;

        // NotNull: null values, empty strings, and whitespace-only strings
        if (rules.NotNull)
        {
            bool isNull = value is null
                || (value is string s && string.IsNullOrWhiteSpace(s));

            if (isNull)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex,
                    Rule = "NotNull",
                    Message = "Value is required",
                    RawValue = value?.ToString()
                });
                hasErrors = true;
            }
        }

        var stringValue = value?.ToString();

        // NotEmpty: non-null but whitespace-only strings
        if (rules.NotEmpty && stringValue is not null && string.IsNullOrWhiteSpace(stringValue))
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                RowNumber = rowNumber,
                ColumnIndex = columnIndex,
                Rule = "NotEmpty",
                Message = "Value must not be empty or whitespace",
                RawValue = stringValue
            });
            hasErrors = true;
        }

        // String length checks
        if (stringValue is not null)
        {
            if (rules.MaxLength.HasValue && stringValue.Length > rules.MaxLength.Value)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex,
                    Rule = "MaxLength",
                    Message = $"Value exceeds maximum length of {rules.MaxLength.Value}",
                    RawValue = stringValue
                });
                hasErrors = true;
            }

            if (rules.MinLength.HasValue && stringValue.Length < rules.MinLength.Value)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex,
                    Rule = "MinLength",
                    Message = $"Value is shorter than minimum length of {rules.MinLength.Value}",
                    RawValue = stringValue
                });
                hasErrors = true;
            }
        }

        // Range checks (numeric values)
        if ((rules.RangeMin.HasValue || rules.RangeMax.HasValue) && value is not null)
        {
            double numericValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);

            if (rules.RangeMin.HasValue && numericValue < rules.RangeMin.Value)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex,
                    Rule = "Range",
                    Message = $"Value {numericValue} is less than minimum {rules.RangeMin.Value}",
                    RawValue = value.ToString()
                });
                hasErrors = true;
            }

            if (rules.RangeMax.HasValue && numericValue > rules.RangeMax.Value)
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex,
                    Rule = "Range",
                    Message = $"Value {numericValue} exceeds maximum {rules.RangeMax.Value}",
                    RawValue = value.ToString()
                });
                hasErrors = true;
            }
        }

        // Pattern check (string values)
        if (rules.Pattern is not null && stringValue is not null)
        {
            var regex = new Regex(rules.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(rules.PatternTimeoutMs));
            if (!regex.IsMatch(stringValue))
            {
                errors.Add(new ValidationError
                {
                    PropertyName = propertyName,
                    RowNumber = rowNumber,
                    ColumnIndex = columnIndex,
                    Rule = "Pattern",
                    Message = $"Value does not match pattern '{rules.Pattern}'",
                    RawValue = stringValue
                });
                hasErrors = true;
            }
        }

        return hasErrors;
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test tests/HeroParser.Tests --filter WriteValidationRunnerTests -f net10.0`
Expected: All 8 tests pass

- [ ] **Step 5: Commit**

---

## Chunk 2: CSV Write-Side Validation

### Task 3: Add ValidationMode to CsvWriteOptions

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Writing/CsvWriteOptions.cs`

- [ ] **Step 1: Add ValidationMode property**

Add `using HeroParser.Validation;` and the property:
```csharp
/// <summary>
/// Gets or sets the validation mode for write operations. Default is <see cref="ValidationMode.Strict"/>.
/// In Strict mode, validation errors throw <see cref="ValidationException"/>.
/// In Lenient mode, records with validation errors are silently skipped.
/// </summary>
public ValidationMode ValidationMode { get; init; } = ValidationMode.Strict;
```

- [ ] **Step 2: Build and verify**

### Task 4: Read [Validate] attributes in CsvRecordWriter.BuildAccessors()

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs`

The key changes:
1. `PropertyAccessor` gains a `WritePropertyValidation?` field
2. `BuildAccessors()` reads `[Validate]` attribute from each property
3. `WriterTemplate` gains validation fields
4. `InstantiateAccessors()` passes validation through

- [ ] **Step 1: Update PropertyAccessor to hold validation**

At line ~1030 in `CsvRecordWriter.cs`, change `PropertyAccessor`:
```csharp
private sealed class PropertyAccessor(
    string memberName, string headerName, string? format,
    bool excludeFromWriteIfAllEmpty,
    Func<object, object?> getter,
    WritePropertyValidation? validation = null)
{
    public string MemberName { get; } = memberName;
    public string HeaderName { get; } = headerName;
    public string? Format { get; } = format;
    public bool ExcludeFromWriteIfAllEmpty { get; } = excludeFromWriteIfAllEmpty;
    public WritePropertyValidation? Validation { get; } = validation;
    private readonly Func<object, object?> getter = getter;
    public object? GetValue(object instance) => getter(instance);
}
```

- [ ] **Step 2: Read [Validate] in BuildAccessors()**

In `BuildAccessors()` (~line 980), after reading other attributes, add:
```csharp
var validateAttr = property.GetCustomAttribute<ValidateAttribute>();
WritePropertyValidation? validation = null;
if (validateAttr is not null)
{
    validation = new WritePropertyValidation(
        validateAttr.NotNull,
        validateAttr.NotEmpty,
        validateAttr.MaxLength >= 0 ? validateAttr.MaxLength : null,
        validateAttr.MinLength >= 0 ? validateAttr.MinLength : null,
        !double.IsNaN(validateAttr.RangeMin) ? validateAttr.RangeMin : null,
        !double.IsNaN(validateAttr.RangeMax) ? validateAttr.RangeMax : null,
        validateAttr.Pattern,
        validateAttr.PatternTimeoutMs);
}
```

And pass `validation` to the `PropertyAccessor` constructor.

- [ ] **Step 3: Update WriterTemplate to carry validation metadata**

Add to `WriterTemplate` record:
```csharp
public sealed record WriterTemplate(
    string MemberName,
    Type SourceType,
    string HeaderName,
    int? AttributeIndex,
    string? Format,
    Func<T, object?> Getter,
    bool ExcludeFromWriteIfAllEmpty = false,
    WritePropertyValidation? Validation = null);
```

Update `InstantiateAccessors()` to pass `template.Validation`.

- [ ] **Step 4: Build and verify**

### Task 5: Enforce validation in WriteRecordInternal

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Writing/CsvRecordWriter.cs` lines 395-448

- [ ] **Step 1: Add validation after value extraction**

In `WriteRecordInternal()`, after the value extraction loop (line 446), before `writer.WriteRowWithFormats()`:

```csharp
// Validate all extracted values before writing
if (writerOptions.ValidationMode != ValidationMode.Lenient)
{
    List<ValidationError>? validationErrors = null;
    for (int i = 0; i < accessors.Length; i++)
    {
        var accessor = accessors[i];
        if (accessor.Validation is { HasAnyRule: true } rules)
        {
            validationErrors ??= [];
            WriteValidationRunner.Validate(valuesBuffer[i], accessor.MemberName, rowNumber, i, rules, validationErrors);
        }
    }

    if (validationErrors is { Count: > 0 })
    {
        if (writerOptions.ValidationMode == ValidationMode.Strict)
            throw new ValidationException(validationErrors);
        return; // Lenient: skip this row
    }
}
```

Same pattern for `WriteRecordInternalAsync()`.

- [ ] **Step 2: Build and verify**

### Task 6: Write CSV write validation tests

**Files:**
- Create: `tests/HeroParser.Tests/Validation/CsvWriteValidationTests.cs`

- [ ] **Step 1: Write tests**

Tests should cover:
- Writing valid records — succeeds
- Writing record with NotNull violation (null/empty decimal mapped via string) — throws `ValidationException` in Strict mode
- Writing record with Range violation — throws
- Writing record with MaxLength violation — throws
- Writing with `ValidationMode.Lenient` — skips invalid records, writes valid ones
- Integration: round-trip read+write with `[Validate]` attributes

- [ ] **Step 2: Run tests and verify they pass**

Run: `dotnet test tests/HeroParser.Tests --filter CsvWriteValidationTests -f net10.0`

- [ ] **Step 3: Commit**

---

## Chunk 3: Source Generator Write Validation

### Task 7: Pass validation metadata in EmitWriterRegistration

**Files:**
- Modify: `src/HeroParser.Generators/CsvRecordBinderGenerator.cs:EmitWriterRegistration()` (~line 860)

- [ ] **Step 1: Emit validation metadata in WriterTemplate**

The source generator already has `ValidationNotNull`, `ValidationNotEmpty`, etc. on `MemberDescriptor`. Pass them to `WriterTemplate`:

```csharp
// In EmitWriterRegistration, after "false)," for ExcludeIfAllEmpty:
if (HasAnyWriteValidation(member))
{
    builder.AppendLine($"new global::HeroParser.Validation.WritePropertyValidation(");
    builder.Indent();
    builder.AppendLine($"{BoolLiteral(member.ValidationNotNull)},");
    builder.AppendLine($"{BoolLiteral(member.ValidationNotEmpty)},");
    builder.AppendLine(member.ValidationMaxLength >= 0 ? $"{member.ValidationMaxLength}," : "null,");
    builder.AppendLine(member.ValidationMinLength >= 0 ? $"{member.ValidationMinLength}," : "null,");
    builder.AppendLine(!double.IsNaN(member.ValidationRangeMin) ? $"{member.ValidationRangeMin}," : "null,");
    builder.AppendLine(!double.IsNaN(member.ValidationRangeMax) ? $"{member.ValidationRangeMax}," : "null,");
    builder.AppendLine(member.ValidationPattern is not null ? $"\"{EscapeString(member.ValidationPattern)}\"," : "null,");
    builder.AppendLine($"{member.ValidationPatternTimeoutMs})),");
    builder.Unindent();
}
else
{
    builder.AppendLine("null),"); // No validation
}
```

- [ ] **Step 2: Run generator tests**

Run: `dotnet test tests/HeroParser.Generators.Tests -f net10.0`

- [ ] **Step 3: Commit**

---

## Chunk 4: FixedWidth Write-Side Validation

### Task 8: Add ValidationMode to FixedWidthWriteOptions + enforce in writer

**Files:**
- Modify: `src/HeroParser/FixedWidths/Writing/FixedWidthWriteOptions.cs`
- Modify: `src/HeroParser/FixedWidths/Writing/FixedWidthRecordWriter.cs`
- Create: `tests/HeroParser.Tests/Validation/FixedWidthWriteValidationTests.cs`

Same pattern as CSV:
1. Add `ValidationMode` to options (default Strict)
2. Read `[Validate]` in property accessor construction
3. Call `WriteValidationRunner` before writing each record
4. Write tests

---

## Chunk 5: Builder API + Final Verification

### Task 9: Add WithValidationMode to write builders

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Writing/CsvRecordWriterBuilder.cs` (if exists, or the Csv.Write facade)
- Modify: `src/HeroParser/FixedWidths/Writing/FixedWidthWriterBuilder.cs` (if exists)

Wire the `ValidationMode` from builder to options.

### Task 10: Full test suite + format check

- [ ] **Step 1: Run all tests**

```bash
dotnet test tests/HeroParser.Tests -f net10.0
dotnet test tests/HeroParser.Generators.Tests -f net10.0
dotnet format --verify-no-changes
```

- [ ] **Step 2: Final commit**
