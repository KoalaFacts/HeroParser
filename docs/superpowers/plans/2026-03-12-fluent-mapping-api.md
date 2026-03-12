# Fluent Mapping API Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add CsvHelper-style fluent mapping API (`CsvMap<T>`, `FixedWidthMap<T>`) for runtime column-to-property configuration with read, write, and validation support.

**Architecture:** Extend existing descriptor infrastructure (`CsvPropertyDescriptor<T>`, `FixedWidthPropertyDescriptor<T>`) with optional validation rules. Fluent maps build descriptors for reading and `WriterTemplate` instances for writing. Compiled expression trees provide property getters/setters at runtime.

**Tech Stack:** C#, Expression Trees, xUnit.v3, multi-framework (net8.0/net9.0/net10.0)

**Spec:** `docs/superpowers/specs/2026-03-12-fluent-mapping-api-design.md`

**Key architectural decisions:**
- **CSV byte-based reads with map:** `CsvDescriptorBinder<T>` only implements `ICsvBinder<char, T>`, and there is no `ByteToCharBinderAdapter`. Map-based reads trade SIMD for flexibility. The byte-signature terminal methods (`FromText(out byte[])`, `FromFile(out byte[])`, `FromStream(out byte[])`) throw `NotSupportedException` when a map is set — users must use `FromText(string)` instead. To support file/stream sources, add new char-returning overloads `FromFile(string path)` and `FromStream(Stream)` that read as string and delegate to `FromTextWithMap`.
- **FixedWidth reader integration:** Add a `FixedWidthRecordBinder<T>.Bind()` overload that accepts an external `IFixedWidthBinder<T>`, since the current static `Bind` method resolves binders internally.
- **Shared validation:** Extract `PropertyValidationRunner` into `HeroParser.Validation` to avoid duplicating validation logic between CSV and FixedWidth descriptor binders.
- **Row exclusion on validation errors:** `BindInto()` returns `false` when validation errors are added, causing the row to be excluded from results (matching source-gen behavior).

---

## Chunk 1: CSV Foundation (Validation types, shared validation, CsvColumnBuilder, CsvMap, descriptor enhancement)

### Task 1: Add CsvPropertyValidation type

**Files:**
- Create: `src/HeroParser/SeparatedValues/Mapping/CsvPropertyValidation.cs`

- [ ] **Step 1: Create the validation container class**

```csharp
using System.Text.RegularExpressions;

namespace HeroParser.SeparatedValues.Mapping;

/// <summary>
/// Runtime validation rules for a CSV property, used by descriptor-based binding.
/// </summary>
public sealed class CsvPropertyValidation
{
    /// <summary>Gets whether the field must be non-null.</summary>
    public bool NotEmpty { get; init; }

    /// <summary>Gets the maximum string length, or null if unconstrained.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Gets the minimum string length, or null if unconstrained.</summary>
    public int? MinLength { get; init; }

    /// <summary>Gets the minimum range value, or null if unconstrained.</summary>
    public double? RangeMin { get; init; }

    /// <summary>Gets the maximum range value, or null if unconstrained.</summary>
    public double? RangeMax { get; init; }

    /// <summary>Gets the regex pattern, or null if unconstrained.</summary>
    public Regex? Pattern { get; init; }

    /// <summary>Gets whether any validation rule is configured.</summary>
    internal bool HasAnyRule =>
        NotEmpty || MaxLength.HasValue || MinLength.HasValue ||
        RangeMin.HasValue || RangeMax.HasValue || Pattern is not null;
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/HeroParser`
Expected: Build succeeds with no errors or warnings.

- [ ] **Step 3: Commit**

```bash
git add src/HeroParser/SeparatedValues/Mapping/CsvPropertyValidation.cs
git commit -m "feat: add CsvPropertyValidation type for runtime validation rules"
```

### Task 2: Add CsvColumnBuilder

**Files:**
- Create: `src/HeroParser/SeparatedValues/Mapping/CsvColumnBuilder.cs`
- Create: `tests/HeroParser.Tests/Mapping/CsvColumnBuilderTests.cs`

- [ ] **Step 1: Write tests for CsvColumnBuilder**

```csharp
using HeroParser.SeparatedValues.Mapping;

namespace HeroParser.Tests.Mapping;

[Trait("Category", "Unit")]
public class CsvColumnBuilderTests
{
    [Fact]
    public void Name_SetsHeaderName()
    {
        var builder = new CsvColumnBuilder();
        builder.Name("Ticker");
        Assert.Equal("Ticker", builder.HeaderName);
    }

    [Fact]
    public void Index_SetsColumnIndex()
    {
        var builder = new CsvColumnBuilder();
        builder.Index(3);
        Assert.Equal(3, builder.ColumnIndex);
    }

    [Fact]
    public void Format_SetsFormatString()
    {
        var builder = new CsvColumnBuilder();
        builder.Format("yyyy-MM-dd");
        Assert.Equal("yyyy-MM-dd", builder.FormatString);
    }

    [Fact]
    public void Required_SetsFlag()
    {
        var builder = new CsvColumnBuilder();
        builder.Required();
        Assert.True(builder.IsRequired);
    }

    [Fact]
    public void Validation_BuildsCorrectly()
    {
        var builder = new CsvColumnBuilder();
        builder.NotEmpty().MaxLength(50).MinLength(1).Range(0, 100).Pattern(@"^\d+$", 500);
        var validation = builder.BuildValidation();
        Assert.NotNull(validation);
        Assert.True(validation!.NotEmpty);
        Assert.Equal(50, validation.MaxLength);
        Assert.Equal(1, validation.MinLength);
        Assert.Equal(0, validation.RangeMin);
        Assert.Equal(100, validation.RangeMax);
        Assert.NotNull(validation.Pattern);
    }

    [Fact]
    public void BuildValidation_ReturnsNull_WhenNoRulesConfigured()
    {
        var builder = new CsvColumnBuilder();
        builder.Name("Col");
        Assert.Null(builder.BuildValidation());
    }

    [Fact]
    public void FluentChaining_ReturnsThis()
    {
        var builder = new CsvColumnBuilder();
        var result = builder.Name("A").Index(0).Format("F2").Required().NotEmpty()
            .MaxLength(10).MinLength(1).Range(0, 99).Pattern(@"\w+");
        Assert.Same(builder, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvColumnBuilderTests" --no-restore`
Expected: FAIL — `CsvColumnBuilder` does not exist yet.

- [ ] **Step 3: Implement CsvColumnBuilder**

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace HeroParser.SeparatedValues.Mapping;

/// <summary>
/// Fluent builder for configuring a single CSV column mapping.
/// </summary>
public sealed class CsvColumnBuilder
{
    /// <summary>Gets the configured header name, or null if not set.</summary>
    public string? HeaderName { get; private set; }

    /// <summary>Gets the configured column index, or null if not set.</summary>
    public int? ColumnIndex { get; private set; }

    /// <summary>Gets the configured format string, or null if not set.</summary>
    public string? FormatString { get; private set; }

    /// <summary>Gets whether the column is required.</summary>
    public bool IsRequired { get; private set; }

    // Validation state
    private bool notEmpty;
    private int? maxLength;
    private int? minLength;
    private double? rangeMin;
    private double? rangeMax;
    private string? pattern;
    private int patternTimeoutMs = 1000;

    /// <summary>Sets the CSV column header name for header-based binding.</summary>
    public CsvColumnBuilder Name(string name) { HeaderName = name; return this; }

    /// <summary>Sets the CSV column index (0-based) for positional binding.</summary>
    public CsvColumnBuilder Index(int index) { ColumnIndex = index; return this; }

    /// <summary>Sets the format string for parsing and writing (e.g., "yyyy-MM-dd", "F2").</summary>
    public CsvColumnBuilder Format(string format) { FormatString = format; return this; }

    /// <summary>Marks the column as required (non-null value).</summary>
    public CsvColumnBuilder Required() { IsRequired = true; return this; }

    /// <summary>Requires the string value to be non-empty and non-whitespace.</summary>
    public CsvColumnBuilder NotEmpty() { notEmpty = true; return this; }

    /// <summary>Sets the maximum string length.</summary>
    public CsvColumnBuilder MaxLength(int length) { maxLength = length; return this; }

    /// <summary>Sets the minimum string length.</summary>
    public CsvColumnBuilder MinLength(int length) { minLength = length; return this; }

    /// <summary>Sets the valid numeric range (inclusive).</summary>
    public CsvColumnBuilder Range(double min, double max) { rangeMin = min; rangeMax = max; return this; }

    /// <summary>Sets a regex pattern the string value must match.</summary>
    public CsvColumnBuilder Pattern([StringSyntax(StringSyntaxAttribute.Regex)] string regex, int timeoutMs = 1000)
    {
        pattern = regex;
        patternTimeoutMs = timeoutMs;
        return this;
    }

    /// <summary>
    /// Builds a <see cref="CsvPropertyValidation"/> from the configured rules, or null if none set.
    /// </summary>
    internal CsvPropertyValidation? BuildValidation()
    {
        if (!notEmpty && !maxLength.HasValue && !minLength.HasValue &&
            !rangeMin.HasValue && !rangeMax.HasValue && pattern is null)
            return null;

        return new CsvPropertyValidation
        {
            NotEmpty = notEmpty,
            MaxLength = maxLength,
            MinLength = minLength,
            RangeMin = rangeMin,
            RangeMax = rangeMax,
            Pattern = pattern is not null
                ? new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(patternTimeoutMs))
                : null
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvColumnBuilderTests"`
Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HeroParser/SeparatedValues/Mapping/CsvColumnBuilder.cs tests/HeroParser.Tests/Mapping/CsvColumnBuilderTests.cs
git commit -m "feat: add CsvColumnBuilder for fluent CSV column configuration"
```

### Task 3: Enhance CsvPropertyDescriptor with validation

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Reading/Shared/CsvPropertyDescriptor.cs`

- [ ] **Step 1: Add validation parameter to CsvPropertyDescriptor**

In `src/HeroParser/SeparatedValues/Reading/Shared/CsvPropertyDescriptor.cs`, add a `using HeroParser.SeparatedValues.Mapping;` and extend the primary constructor:

Change the primary constructor (line 22-26) from:
```csharp
public readonly struct CsvPropertyDescriptor<T>(
    string name,
    int columnIndex,
    CsvPropertySetter<T> setter,
    bool isRequired = false)
```
to:
```csharp
public readonly struct CsvPropertyDescriptor<T>(
    string name,
    int columnIndex,
    CsvPropertySetter<T> setter,
    bool isRequired = false,
    CsvPropertyValidation? validation = null)
```

Add a new property after `IsRequired`:
```csharp
/// <summary>Gets the validation rules for this property, or null if none configured.</summary>
public CsvPropertyValidation? Validation { get; } = validation;
```

Update the header-based constructor (line 43-48) to pass `validation: null`:
```csharp
public CsvPropertyDescriptor(
    string name,
    CsvPropertySetter<T> setter,
    bool isRequired = false)
    : this(name, -1, setter, isRequired, validation: null)
{
}
```

Update `WithResolvedIndices` (line 81-85) to propagate validation:
```csharp
resolvedProperties[i] = new CsvPropertyDescriptor<T>(
    prop.Name,
    columnIndices[i],
    prop.Setter,
    prop.IsRequired,
    prop.Validation);
```

- [ ] **Step 2: Verify build and existing tests still pass**

Run: `dotnet build && dotnet test --filter Category=Unit`
Expected: Build succeeds. All existing tests pass (the new parameter has a default value of null).

- [ ] **Step 3: Commit**

```bash
git add src/HeroParser/SeparatedValues/Reading/Shared/CsvPropertyDescriptor.cs
git commit -m "feat: add optional validation parameter to CsvPropertyDescriptor"
```

### Task 4: Add shared PropertyValidationRunner and validation logic to CsvDescriptorBinder

**Files:**
- Create: `src/HeroParser/Validation/PropertyValidationRunner.cs`
- Modify: `src/HeroParser/SeparatedValues/Reading/Binders/CsvDescriptorBinder.cs`
- Create: `tests/HeroParser.Tests/Mapping/CsvDescriptorBinderValidationTests.cs`

- [ ] **Step 1: Create shared PropertyValidationRunner**

This shared class avoids duplicating validation logic between CSV and FixedWidth binders. Place in `src/HeroParser/Validation/PropertyValidationRunner.cs`:

```csharp
using System.Text.RegularExpressions;

namespace HeroParser.Validation;

/// <summary>
/// Shared validation runner that checks field values against configured rules.
/// Used by both CSV and FixedWidth descriptor binders.
/// </summary>
internal static class PropertyValidationRunner
{
    /// <summary>
    /// Validates a field value against the given rules and appends any errors.
    /// Returns true if any validation errors were added (caller should exclude the row).
    /// </summary>
    public static bool Validate(
        string value,
        string propertyName,
        int rowNumber,
        int columnIndex,
        string? columnName,
        bool notEmpty,
        int? minLength,
        int? maxLength,
        double? rangeMin,
        double? rangeMax,
        Regex? pattern,
        List<ValidationError> errors)
    {
        int initialCount = errors.Count;

        if (notEmpty && string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = columnName,
                RawValue = value,
                Rule = "NotEmpty",
                Message = $"Field '{propertyName}' must not be empty or whitespace.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
        }

        if (minLength.HasValue && value.Length < minLength.Value)
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = columnName,
                RawValue = value,
                Rule = "MinLength",
                Message = $"Field '{propertyName}' length {value.Length} is less than minimum {minLength.Value}.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
        }

        if (maxLength.HasValue && value.Length > maxLength.Value)
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = columnName,
                RawValue = value,
                Rule = "MaxLength",
                Message = $"Field '{propertyName}' length {value.Length} exceeds maximum {maxLength.Value}.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
        }

        if (rangeMin.HasValue || rangeMax.HasValue)
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
            {
                if (rangeMin.HasValue && numericValue < rangeMin.Value)
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = propertyName,
                        ColumnName = columnName,
                        RawValue = value,
                        Rule = "Range",
                        Message = $"Field '{propertyName}' value {numericValue} is less than minimum {rangeMin.Value}.",
                        RowNumber = rowNumber,
                        ColumnIndex = columnIndex
                    });
                }
                if (rangeMax.HasValue && numericValue > rangeMax.Value)
                {
                    errors.Add(new ValidationError
                    {
                        PropertyName = propertyName,
                        ColumnName = columnName,
                        RawValue = value,
                        Rule = "Range",
                        Message = $"Field '{propertyName}' value {numericValue} exceeds maximum {rangeMax.Value}.",
                        RowNumber = rowNumber,
                        ColumnIndex = columnIndex
                    });
                }
            }
        }

        if (pattern is { } regex && !regex.IsMatch(value))
        {
            errors.Add(new ValidationError
            {
                PropertyName = propertyName,
                ColumnName = columnName,
                RawValue = value,
                Rule = "Pattern",
                Message = $"Field '{propertyName}' value '{value}' does not match pattern '{regex}'.",
                RowNumber = rowNumber,
                ColumnIndex = columnIndex
            });
        }

        return errors.Count > initialCount;
    }
}
```

**Note about `ColumnName`:** `ValidationError` has a `ColumnName` property (nullable). For CSV, set it to `prop.Name` (the header name). For FixedWidth, set it to `prop.Name` (the property name, since there are no headers).

**Note about `NotEmpty` on numeric fields:** `NotEmpty` validates the raw string value *before* parsing. For numeric fields like `int`, the setter will have already parsed the value before validation runs. If parsing succeeds but the string was empty/whitespace, the value will be 0 (default). Consider that `NotEmpty` is most useful for string fields — document this limitation in the column builder XML docs.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/HeroParser`

- [ ] **Step 3: Write tests for descriptor binder validation**

Create `tests/HeroParser.Tests/Mapping/CsvDescriptorBinderValidationTests.cs`. These tests construct a `CsvRecordDescriptor<T>` manually with validation rules and verify the binder produces `ValidationError`s.

Use a simple test record:
```csharp
public class SimpleRecord
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}
```

Tests:
- `NotEmpty_WhitespaceField_AddsValidationError` — bind a row with whitespace, check NotEmpty fires
- `MaxLength_ExceedsLimit_AddsValidationError` — bind a string field exceeding max length
- `Range_OutOfBounds_AddsValidationError` — bind a numeric field outside range
- `Pattern_NoMatch_AddsValidationError` — bind a string field not matching pattern
- `NoValidation_NoErrors` — bind with no validation rules, errors list stays empty
- `Validation_ValidData_NoErrors` — bind with all rules satisfied, no errors
- `ValidationErrors_CausesRowExclusion` — when validation adds errors, `BindInto()` returns `false`, row is excluded

Verify each `ValidationError` includes `ColumnName = prop.Name`.

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvDescriptorBinderValidationTests" --no-restore`
Expected: FAIL — validation not implemented yet.

- [ ] **Step 5: Add validation logic to CsvDescriptorBinder.BindInto()**

In `src/HeroParser/SeparatedValues/Reading/Binders/CsvDescriptorBinder.cs`, add `using HeroParser.SeparatedValues.Mapping;` and `using HeroParser.Validation;` at the top.

Inside `BindInto()`, after the successful setter call (after `prop.Setter(ref instance, span, cultureLocal);`), add validation. Track whether any errors were added and return `false` if so (row exclusion):

Place the validation block **after the entire try/catch block** (not inside it), so that regex timeout exceptions from `Pattern` validation are not caught by the setter's exception handler:

```csharp
// After the try/catch for the setter, still inside the ((uint)idx < (uint)columnCount) block:
if (prop.Validation is { } validation && errors is not null)
{
    var fieldStr = new string(span);
    // Use null for ColumnName when using index-based (headerless) binding
    string? colName = resolvedProperties == descriptor.Properties && !descriptor.UsesHeaderBinding
        ? null : prop.Name;
    hasErrors |= PropertyValidationRunner.Validate(
        fieldStr, prop.Name, rowNumber, idx,
        columnName: colName,
        validation.NotEmpty, validation.MinLength, validation.MaxLength,
        validation.RangeMin, validation.RangeMax, validation.Pattern,
        errors);
}
```

**Important placement:** This block goes after the `catch` block's closing brace, NOT inside the `try`. This prevents regex `RegexMatchTimeoutException` from being swallowed by the setter's catch handler.

**Important `ColumnName` semantics:** Per `ValidationError` docs, `ColumnName` is "the column name from the header row, or `null` when no header is present." For index-based binding (no headers), pass `null`. For header-based binding, pass `prop.Name` (which is the header name).

Add `bool hasErrors = false;` at the top of `BindInto()`. Change the `return true;` at the bottom to `return !hasErrors;`.

**Row exclusion behavior:** When `BindInto()` returns `false`, the record reader skips the row and does not include it in results. This matches the source-generated validator behavior.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvDescriptorBinderValidationTests"`
Expected: All tests PASS.

- [ ] **Step 7: Run full test suite to verify no regressions**

Run: `dotnet test`
Expected: All existing tests still pass.

- [ ] **Step 8: Commit**

```bash
git add src/HeroParser/Validation/PropertyValidationRunner.cs src/HeroParser/SeparatedValues/Reading/Binders/CsvDescriptorBinder.cs tests/HeroParser.Tests/Mapping/CsvDescriptorBinderValidationTests.cs
git commit -m "feat: add shared PropertyValidationRunner and validation logic to CsvDescriptorBinder"
```

### Task 5: Implement CsvMap<T>

**Files:**
- Create: `src/HeroParser/SeparatedValues/Mapping/CsvMap.cs`
- Create: `tests/HeroParser.Tests/Mapping/CsvMapTests.cs`

- [ ] **Step 1: Write tests for CsvMap<T>**

Test record (reuse or create in test file):
```csharp
public class Trade
{
    public string Symbol { get; set; } = "";
    public decimal Price { get; set; }
    public DateTime Date { get; set; }
}
```

Tests:
- `Map_WithName_BuildsDescriptor_WithHeaderBinding` — verify `BuildReadDescriptor()` produces a descriptor with correct column names and `UsesHeaderBinding = true`
- `Map_WithIndex_BuildsDescriptor_WithIndexBinding` — verify index-based binding
- `Map_WithValidation_BuildsDescriptor_WithValidationRules` — verify validation is carried through
- `Map_WithFormat_BuildsWriteTemplates_WithFormat` — verify `BuildWriteTemplates()` produces templates with format strings
- `Map_ThrowsOnDuplicateProperty` — mapping same property twice throws `InvalidOperationException`
- `Map_Subclass_WorksLikeInline` — subclass pattern produces same descriptor
- `Map_FluentChaining_ReturnsThis` — all Map() calls return same instance

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvMapTests" --no-restore`
Expected: FAIL — `CsvMap<T>` does not exist.

- [ ] **Step 3: Implement CsvMap<T>**

Key implementation details:
- `CsvMap<T> where T : new()` class with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` attributes
- Internal `List<MappedProperty>` storing per-property config
- `MappedProperty` record: `PropertyName`, `PropertyInfo`, `HeaderName`, `ColumnIndex`, `FormatString`, `IsRequired`, `CsvPropertyValidation?`
- `Map<TProperty>(Expression<Func<T, TProperty>> property, Action<CsvColumnBuilder>? configure = null)`:
  - Extract `PropertyInfo` from expression via `((MemberExpression)lambda.Body).Member`
  - Create `CsvColumnBuilder`, call configure lambda
  - Check for duplicates (same `PropertyInfo.Name` already mapped → throw `InvalidOperationException`)
  - Store in list, return `this`

**`BuildReadDescriptor()` — setter construction:**

`CsvPropertySetter<T>` signature: `delegate void (ref T instance, ReadOnlySpan<char> value, CultureInfo culture)`. Since `ReadOnlySpan<char>` cannot be used in expression trees, build setters manually:

1. **Property assignment** — Use `PropertyInfo.SetValue()` for classes (ref T is boxed to object). Alternatively, compile an `Action<T, TProperty>` from an expression tree and wrap it:
   ```csharp
   // Build: (T instance, TProperty val) => instance.Prop = val
   var param = Expression.Parameter(typeof(T), "instance");
   var valParam = Expression.Parameter(typeof(TProperty), "val");
   var assign = Expression.Assign(Expression.Property(param, propertyInfo), valParam);
   var setter = Expression.Lambda<Action<T, TProperty>>(assign, param, valParam).Compile();
   ```

2. **Type-specific parser** — Create a static parser method per type:
   - `string`: `(ReadOnlySpan<char> v, CultureInfo c) => new string(v)`
   - `int`/`long`/`short`/`byte`: `int.Parse(v, NumberStyles.Integer, c)` etc.
   - `decimal`/`double`/`float`: `decimal.Parse(v, NumberStyles.Number, c)` etc.
   - `bool`: `bool.Parse(v)`
   - `DateTime`: format? `DateTime.ParseExact(v, format, c)` : `DateTime.Parse(v, c)`
   - `DateOnly`/`TimeOnly`: similar pattern (net8.0+ only)
   - `Guid`: `Guid.Parse(v)`
   - `Enum`: `Enum.Parse<TEnum>(v)`
   - Nullable `T?`: if span is empty/whitespace, assign default; else parse inner type

3. **Combine** into a `CsvPropertySetter<T>`:
   ```csharp
   // propertySetter: Action<T, TProperty>, parser: Func<ReadOnlySpan<char>, CultureInfo, TProperty>
   return (ref T instance, ReadOnlySpan<char> value, CultureInfo culture) =>
   {
       var parsed = parser(value, culture);
       propertySetter(instance, parsed);
   };
   ```

4. Return `new CsvRecordDescriptor<T>(descriptors, () => new T())`

**Struct records:** `CsvPropertySetter<T>` takes `ref T`, which is essential for structs. `Action<T, TProperty>` does NOT take ref, so the compiled lambda silently produces a no-op for value types. **Resolution:** Restrict `CsvMap<T>` to `where T : class, new()` (add `class` constraint). This is acceptable because:
- Fluent maps target runtime scenarios where classes are the norm
- The attribute-based + source-gen path already handles struct records
- This avoids a subtle correctness bug that would be hard to diagnose

Apply the same `class` constraint to `FixedWidthMap<T>` in Task 12.

**`BuildWriteTemplates()` — getter construction:**

Produces `CsvRecordWriter<T>.WriterTemplate[]`. The `WriterTemplate` record has fields:
- `MemberName` (string) — property name
- `SourceType` (Type) — `typeof(TProperty)`
- `HeaderName` (string) — from `CsvColumnBuilder.HeaderName ?? propertyInfo.Name`
- `AttributeIndex` (int?) — from `CsvColumnBuilder.ColumnIndex`
- `Format` (string?) — from `CsvColumnBuilder.FormatString`
- `Getter` (Func<T, object?>) — compiled expression tree

Build getter:
```csharp
// (T instance) => (object?)instance.Prop
var param = Expression.Parameter(typeof(T), "instance");
var access = Expression.Property(param, propertyInfo);
var boxed = Expression.Convert(access, typeof(object));
var getter = Expression.Lambda<Func<T, object?>>(boxed, param).Compile();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvMapTests"`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/HeroParser/SeparatedValues/Mapping/CsvMap.cs tests/HeroParser.Tests/Mapping/CsvMapTests.cs
git commit -m "feat: implement CsvMap<T> fluent mapping with read descriptors and write templates"
```

### Task 6: Integrate CsvMap with CsvRecordReaderBuilder (WithMap + inline Map)

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Reading/Records/CsvRecordReaderBuilder.cs` (add `WithMap`, `Map` methods and `csvMap` field)
- Modify: `src/HeroParser/SeparatedValues/Reading/Records/CsvRecordReaderBuilder.Terminal.cs` (use map-provided binder when available)
- Create: `tests/HeroParser.Tests/Mapping/CsvMapIntegrationTests.cs`

- [ ] **Step 1: Write integration tests for CSV map reading**

Tests:
- `WithMap_FromText_ReadsRecords_ByHeaderName` — map with `.Name()`, read CSV with headers, verify records
- `WithMap_FromText_ReadsRecords_ByIndex` — map with `.Index()`, read headerless CSV, verify records
- `WithMap_FromText_ValidationErrors_Collected` — map with validation rules, read CSV with invalid data, verify errors on `reader.Errors`
- `InlineMap_FromText_ReadsRecords` — use `.Map()` on builder directly, verify records
- `WithMap_SubclassMap_ReadsRecords` — use subclassed `CsvMap<T>`, verify records

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvMapIntegrationTests" --no-restore`
Expected: FAIL — `WithMap` and `Map` methods don't exist on builder.

- [ ] **Step 3: Add WithMap and Map methods to CsvRecordReaderBuilder**

In `src/HeroParser/SeparatedValues/Reading/Records/CsvRecordReaderBuilder.cs`:
- Add field: `private CsvMap<T>? csvMap;`
- Add methods:
```csharp
[RequiresUnreferencedCode("Fluent mapping uses reflection. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
[RequiresDynamicCode("Fluent mapping uses expression compilation. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
public CsvRecordReaderBuilder<T> WithMap(CsvMap<T> map)
{
    csvMap = map;
    return this;
}

[RequiresUnreferencedCode("Fluent mapping uses reflection. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
[RequiresDynamicCode("Fluent mapping uses expression compilation. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
public CsvRecordReaderBuilder<T> Map<TProperty>(
    Expression<Func<T, TProperty>> property,
    Action<CsvColumnBuilder>? configure = null)
{
    csvMap ??= new CsvMap<T>();
    csvMap.Map(property, configure);
    return this;
}
```

- [ ] **Step 4: Modify terminal methods to use map-provided binder**

In `src/HeroParser/SeparatedValues/Reading/Records/CsvRecordReaderBuilder.Terminal.cs`, add a private helper method:

```csharp
private CsvRecordReader<char, T> FromTextWithMap(string csvText)
{
    var (parserOptions, recordOptions) = GetOptions();
    var descriptor = csvMap!.BuildReadDescriptor();
    var binder = new CsvDescriptorBinder<T>(descriptor, recordOptions);
    // Use char-based row reader (not SIMD byte path)
    var rowReader = new CsvRowReader<char>(csvText.AsMemory(), parserOptions);
    return new CsvRecordReader<char, T>(rowReader, binder, recordOptions);
}
```

**Note:** The exact `CsvRecordReader<char, T>` constructor signature and `CsvRowReader<char>` construction must match what `Csv.DeserializeRecords<T>(string)` does internally. Check the source for the correct wiring.

Then modify each terminal method:

1. **`FromText(string csvText)` → `CsvRecordReader<char, T>`**: Already returns char-based reader. Add map check at top:
   ```csharp
   if (csvMap is not null) return FromTextWithMap(csvText);
   ```

2. **`FromText(string csvText, out byte[] textBytes)` → `CsvRecordReader<byte, T>`**: This returns a byte-based reader, but `CsvDescriptorBinder<T>` only implements `ICsvBinder<char, T>`. **When map is set, fall back to char path** — change return type approach:
   ```csharp
   // Cannot return CsvRecordReader<byte, T> with map because descriptor binder is char-only.
   // Throw NotSupportedException with guidance message:
   if (csvMap is not null)
       throw new NotSupportedException(
           "Fluent map-based reading does not support byte-based APIs. Use FromText(string) instead.");
   ```
   Alternatively, encode the string to bytes then decode back — wasteful, so prefer the exception approach.

3. **`FromFile(string path, out byte[] fileBytes)` → `CsvRecordReader<byte, T>`**: Same situation as #2. When map is set, throw `NotSupportedException`.

4. **`FromStream(Stream stream, out byte[] streamBytes, bool leaveOpen)` → `CsvRecordReader<byte, T>`**: Same — throw `NotSupportedException` when map is set.

**Design decision:** The byte-based terminal methods (`FromText(out byte[])`, `FromFile(out byte[])`, `FromStream(out byte[])`) are incompatible with map-based reading because `CsvDescriptorBinder<T>` only implements `ICsvBinder<char, T>` and no char→byte adapter exists. Rather than silently degrading performance (SIMD loss), throw a clear exception guiding users to the char-based `FromText(string)` overload. The user explicitly accepted this trade-off during design.

**New char-returning overloads for file/stream sources:** To support file and stream sources with maps, add new overloads that read to string and use the char path:

```csharp
/// <summary>Reads records from a file using the char-based path (for map-based reading).</summary>
public CsvRecordReader<char, T> FromFile(string path)
{
    ArgumentNullException.ThrowIfNull(path);
    if (csvMap is null)
        throw new InvalidOperationException("FromFile(string) without out parameter requires a map. Use FromFile(string, out byte[]) for non-map reads.");
    var csvText = File.ReadAllText(path);
    return FromTextWithMap(csvText);
}

/// <summary>Reads records from a stream using the char-based path (for map-based reading).</summary>
public CsvRecordReader<char, T> FromStream(Stream stream, bool leaveOpen = true)
{
    ArgumentNullException.ThrowIfNull(stream);
    if (csvMap is null)
        throw new InvalidOperationException("FromStream without out parameter requires a map. Use FromStream(Stream, out byte[]) for non-map reads.");
    using var reader = new StreamReader(stream, leaveOpen: leaveOpen);
    var csvText = reader.ReadToEnd();
    return FromTextWithMap(csvText);
}
```

These overloads exist only for map-based reading. The byte-returning overloads continue to work for non-map reads. Integration tests should cover `FromFile(path)` and `FromStream(stream)` with maps.

- [ ] **Step 5: Run integration tests to verify they pass**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvMapIntegrationTests"`
Expected: All tests PASS.

- [ ] **Step 6: Run full test suite**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/HeroParser/SeparatedValues/Reading/Records/CsvRecordReaderBuilder.cs src/HeroParser/SeparatedValues/Reading/Records/CsvRecordReaderBuilder.Terminal.cs tests/HeroParser.Tests/Mapping/CsvMapIntegrationTests.cs
git commit -m "feat: integrate CsvMap with reader builder (WithMap + inline Map)"
```

### Task 7: Integrate CsvMap with CsvWriterBuilder (WithMap for write)

**Files:**
- Modify: `src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs` (add `WithMap` method, use map-provided templates)
- Add tests to: `tests/HeroParser.Tests/Mapping/CsvMapIntegrationTests.cs`

- [ ] **Step 1: Write integration tests for CSV map writing**

Add to `CsvMapIntegrationTests.cs`:
- `WithMap_ToText_WritesRecords_WithHeaderNames` — map with `.Name()`, write to text, verify CSV output uses mapped header names
- `WithMap_RoundTrip_ReadThenWrite` — read with map, write with same map, verify round-trip fidelity

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvMapIntegrationTests" --no-restore`
Expected: New tests FAIL — `WithMap` doesn't exist on writer builder.

- [ ] **Step 3: Add WithMap to CsvWriterBuilder**

In `src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs`:
- Add field: `private CsvMap<T>? csvMap;`
- Add method:
```csharp
[RequiresUnreferencedCode("Fluent mapping uses reflection. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
[RequiresDynamicCode("Fluent mapping uses expression compilation. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
public CsvWriterBuilder<T> WithMap(CsvMap<T> map)
{
    csvMap = map;
    return this;
}
```

- [ ] **Step 4: Extract helper and modify all terminal methods**

First, add a private helper method to centralize writer resolution:
```csharp
private CsvRecordWriter<T> GetRecordWriter(CsvWriteOptions? options)
{
    return csvMap is not null
        ? CsvRecordWriter<T>.CreateFromTemplates(options, csvMap.BuildWriteTemplates())
        : CsvRecordWriterFactory.GetWriter<T>(options);
}
```

Then replace `CsvRecordWriterFactory.GetWriter<T>(options)` with `GetRecordWriter(options)` in **all** terminal methods. The actual methods in `CsvWriterBuilder.cs` that call `GetWriter` are:
- `ToText(IEnumerable<T> records)`
- `ToTextAsync(IEnumerable<T> records, CancellationToken)`
- `ToFile(string path, IEnumerable<T> records)`
- `ToStream(Stream, IEnumerable<T>, bool leaveOpen)`
- `ToWriter(TextWriter, IEnumerable<T>)`
- `ToFileAsync(string path, IEnumerable<T>, CancellationToken)`
- `ToStreamAsync(Stream, IEnumerable<T>, CancellationToken)`
- `ToStreamAsyncStreaming(Stream, IAsyncEnumerable<T>, ...)` (2 overloads)

Each one should use `GetRecordWriter(options)` instead of the factory directly.

**Note:** `AttributeIndex` on `WriterTemplate` is unused in the write path — column order in output follows the order `Map()` calls are made on `CsvMap<T>`, NOT the `ColumnIndex` value. Document this in the `CsvMap<T>` class XML docs.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/HeroParser.Tests --filter "FullyQualifiedName~CsvMapIntegrationTests"`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/HeroParser/SeparatedValues/Writing/CsvWriterBuilder.cs tests/HeroParser.Tests/Mapping/CsvMapIntegrationTests.cs
git commit -m "feat: integrate CsvMap with writer builder (WithMap for write)"
```

## Chunk 2: FixedWidth Foundation (mirror CSV pattern)

### Task 8: Add FixedWidthPropertyValidation type

**Files:**
- Create: `src/HeroParser/FixedWidths/Mapping/FixedWidthPropertyValidation.cs`

Mirror `CsvPropertyValidation` exactly (same properties, different namespace). Use namespace `HeroParser.FixedWidths.Mapping`.

- [ ] **Step 1: Create the file**
- [ ] **Step 2: Verify build**

Run: `dotnet build src/HeroParser`

- [ ] **Step 3: Commit**

```bash
git add src/HeroParser/FixedWidths/Mapping/FixedWidthPropertyValidation.cs
git commit -m "feat: add FixedWidthPropertyValidation type"
```

### Task 9: Add FixedWidthColumnBuilder

**Files:**
- Create: `src/HeroParser/FixedWidths/Mapping/FixedWidthColumnBuilder.cs`
- Create: `tests/HeroParser.Tests/Mapping/FixedWidthColumnBuilderTests.cs`

Mirror `CsvColumnBuilder` with these differences:
- **Remove:** `Name()`, `Index()` (FixedWidth uses positional layout, not headers)
- **Add:** `Start(int)`, `Length(int)`, `End(int)`, `PadChar(char)`, `Alignment(FieldAlignment)`, `Format(string)`
- **Keep:** All validation methods identical (`NotEmpty()`, `MaxLength()`, `MinLength()`, `Range()`, `Pattern()`, `Required()`)

`End` semantics: exclusive end position. `End` is an alternative to `Length`. If both `Length` and `End` are set, `End` takes precedence. Compute `Length = End - Start`.

`Format` semantics: format string passed through to `WriterTemplate.Format` and used during read parsing for DateTime/DateOnly/TimeOnly fields.

Properties exposed (for `FixedWidthMap` to read):
- `StartPosition` (int?) — set via `Start()`
- `FieldLength` (int?) — set via `Length()` or computed from `End()`
- `FieldPadChar` (char?) — set via `PadChar()`
- `FieldAlignment` (FieldAlignment?) — set via `Alignment()`
- `FormatString` (string?) — set via `Format()`
- `IsRequired` (bool) — set via `Required()`
- `BuildValidation()` → `FixedWidthPropertyValidation?`

- [ ] **Step 1: Write tests**

Tests should include:
- `Start_SetsStartPosition`
- `Length_SetsFieldLength`
- `End_ComputesLengthFromStart` — `Start(0).End(10)` → length 10
- `End_OverridesLength` — `Start(0).Length(5).End(10)` → length 10 (End wins)
- `PadChar_SetsPadChar`
- `Alignment_SetsAlignment`
- `Format_SetsFormatString`
- `Validation_BuildsCorrectly`
- `BuildValidation_ReturnsNull_WhenNoRulesConfigured`
- `FluentChaining_ReturnsThis`

- [ ] **Step 2: Run tests to verify they fail**
- [ ] **Step 3: Implement FixedWidthColumnBuilder**
- [ ] **Step 4: Run tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add src/HeroParser/FixedWidths/Mapping/FixedWidthColumnBuilder.cs tests/HeroParser.Tests/Mapping/FixedWidthColumnBuilderTests.cs
git commit -m "feat: add FixedWidthColumnBuilder for fluent fixed-width column configuration"
```

### Task 10: Enhance FixedWidthPropertyDescriptor with validation

**Files:**
- Modify: `src/HeroParser/FixedWidths/Records/Binding/FixedWidthPropertyDescriptor.cs`

Same pattern as Task 3. The current primary constructor in `src/HeroParser/FixedWidths/Records/Binding/FixedWidthPropertyDescriptor.cs` is:

```csharp
public readonly struct FixedWidthPropertyDescriptor<T>(
    string name,
    int start,
    int length,
    char padChar,
    FieldAlignment alignment,
    FixedWidthPropertySetter<T> setter,
    bool isRequired = false)
```

Change to:
```csharp
public readonly struct FixedWidthPropertyDescriptor<T>(
    string name,
    int start,
    int length,
    char padChar,
    FieldAlignment alignment,
    FixedWidthPropertySetter<T> setter,
    bool isRequired = false,
    FixedWidthPropertyValidation? validation = null)
```

Add property:
```csharp
/// <summary>Gets the validation rules for this property, or null if none configured.</summary>
public FixedWidthPropertyValidation? Validation { get; } = validation;
```

Add `using HeroParser.FixedWidths.Mapping;` at the top.

No secondary constructor changes needed (FixedWidth doesn't have a header-based constructor). Unlike CSV's `CsvRecordDescriptor<T>.WithResolvedIndices()`, FixedWidth has no equivalent method that needs to propagate the new field.

- [ ] **Step 1: Add validation parameter and property**
- [ ] **Step 2: Verify build and existing tests pass**

Run: `dotnet build && dotnet test --filter Category=Unit`

- [ ] **Step 3: Commit**

```bash
git add src/HeroParser/FixedWidths/Records/Binding/FixedWidthPropertyDescriptor.cs
git commit -m "feat: add optional validation parameter to FixedWidthPropertyDescriptor"
```

### Task 11: Add validation logic to FixedWidthDescriptorBinder

**Files:**
- Modify: `src/HeroParser/FixedWidths/Records/Binding/FixedWidthDescriptorBinder.cs`
- Create: `tests/HeroParser.Tests/Mapping/FixedWidthDescriptorBinderValidationTests.cs`

Same pattern as Task 4, but using the shared `PropertyValidationRunner` from `HeroParser.Validation` (created in Task 4). No need to duplicate the validation logic — call `PropertyValidationRunner.Validate()` directly.

In `BindInto()`, after the setter call:
```csharp
if (prop.Validation is { } validation && errors is not null)
{
    var fieldStr = new string(span);
    hasErrors |= PropertyValidationRunner.Validate(
        fieldStr, prop.Name, row.RecordNumber, i,
        columnName: prop.Name,
        validation.NotEmpty, validation.MinLength, validation.MaxLength,
        validation.RangeMin, validation.RangeMax, validation.Pattern,
        errors);
}
```

Add `bool hasErrors = false;` at top of `BindInto()`. Change `return true;` to `return !hasErrors;`.

**Note:** FixedWidth uses `row.RecordNumber` for the row number and `i` (loop index) for column index, since fixed-width doesn't have column indices like CSV.

- [ ] **Step 1: Write tests** (same patterns as Task 4 but with `FixedWidthCharSpanRow`)
- [ ] **Step 2: Run tests to verify they fail**
- [ ] **Step 3: Add validation logic using PropertyValidationRunner**
- [ ] **Step 4: Run tests to verify they pass**
- [ ] **Step 5: Run full test suite**
- [ ] **Step 6: Commit**

```bash
git add src/HeroParser/FixedWidths/Records/Binding/FixedWidthDescriptorBinder.cs tests/HeroParser.Tests/Mapping/FixedWidthDescriptorBinderValidationTests.cs
git commit -m "feat: add validation logic to FixedWidthDescriptorBinder"
```

### Task 12: Implement FixedWidthMap<T>

**Files:**
- Create: `src/HeroParser/FixedWidths/Mapping/FixedWidthMap.cs`
- Create: `tests/HeroParser.Tests/Mapping/FixedWidthMapTests.cs`

Same pattern as Task 5 but produces `FixedWidthRecordDescriptor<T>` for reading and `FixedWidthRecordWriter<T>.WriterTemplate[]` for writing.

**Key differences from CsvMap:**
- Each mapped property requires `Start` and either `Length` or `End` (validated at build time)
- No header binding — all positional

**`BuildReadDescriptor()`** produces `FixedWidthPropertyDescriptor<T>[]`. Each descriptor needs:
- `name` ← property name
- `start` ← from `FixedWidthColumnBuilder.StartPosition`
- `length` ← from `FixedWidthColumnBuilder.FieldLength` (or computed from End)
- `padChar` ← from `FixedWidthColumnBuilder.FieldPadChar ?? ' '`
- `alignment` ← from `FixedWidthColumnBuilder.FieldAlignment ?? FieldAlignment.Left`
- `setter` ← built same way as CsvMap (expression tree + type parser)
- `isRequired` ← from builder
- `validation` ← from builder

The setter for FixedWidth uses `FixedWidthPropertySetter<T>` which has the same signature as `CsvPropertySetter<T>`: `(ref T instance, ReadOnlySpan<char> value, CultureInfo culture)`. Reuse the same setter factory approach from Task 5.

Returns `new FixedWidthRecordDescriptor<T>(descriptors, () => new T())`.

**`BuildWriteTemplates()`** produces `FixedWidthRecordWriter<T>.WriterTemplate[]`. Each template has 8 fields:
- `MemberName` (string) ← property name
- `SourceType` (Type) ← `typeof(TProperty)`
- `Start` (int) ← from column builder
- `Length` (int) ← from column builder
- `Alignment` (FieldAlignment) ← from column builder or default `Left`
- `PadChar` (char) ← from column builder or default `' '`
- `Format` (string?) ← from column builder
- `Getter` (Func<T, object?>) ← compiled expression tree (same approach as CsvMap)

- [ ] **Step 1: Write tests**
- [ ] **Step 2: Run tests to verify they fail**
- [ ] **Step 3: Implement FixedWidthMap<T>**
- [ ] **Step 4: Run tests to verify they pass**
- [ ] **Step 5: Commit**

```bash
git add src/HeroParser/FixedWidths/Mapping/FixedWidthMap.cs tests/HeroParser.Tests/Mapping/FixedWidthMapTests.cs
git commit -m "feat: implement FixedWidthMap<T> fluent mapping"
```

### Task 13: Integrate FixedWidthMap with reader and writer builders

**Files:**
- Modify: `src/HeroParser/FixedWidths/Records/FixedWidthReaderBuilder.cs` (add `WithMap`, `Map` methods)
- Modify: `src/HeroParser/FixedWidths/Writing/FixedWidthWriterBuilder.cs` (add `WithMap` method)
- Create: `tests/HeroParser.Tests/Mapping/FixedWidthMapIntegrationTests.cs`

**Reader builder integration:**

The current `FixedWidthReaderBuilder<T>.FromText()` calls `FixedWidthRecordBinder<T>.Bind(reader, culture, ...)` which is a static method that internally resolves binders (generated → descriptor → reflection). To inject a map-provided binder, add a new `Bind` overload to `FixedWidthRecordBinder<T>`:

```csharp
// New overload in FixedWidthRecordBinder<T>
public static FixedWidthReadResult<T> Bind(
    FixedWidthCharSpanReader reader,
    IFixedWidthBinder<T> binder,
    IProgress<FixedWidthProgress>? progress = null,
    int progressIntervalRows = 1000)
{
    var errors = new List<ValidationError>();
    var records = BindWithTypedBinder(reader, binder, reader.EstimateRowCount(),
        progress, progressIntervalRows, errors);
    return new FixedWidthReadResult<T>(records, errors);
}
```

Then in `FixedWidthReaderBuilder<T>`:
- Add field: `private FixedWidthMap<T>? fixedWidthMap;`
- Add `WithMap()` and `Map()` methods (same pattern as CSV, with AOT annotations)
- Modify `FromText(string text)`:
  ```csharp
  if (fixedWidthMap is not null)
  {
      var descriptor = fixedWidthMap.BuildReadDescriptor();
      var binder = new FixedWidthDescriptorBinder<T>(descriptor, culture, nullValues);
      return FixedWidthRecordBinder<T>.Bind(
          FixedWidthFactory.ReadFromText(text, options), binder, progress, progressIntervalRows);
  }
  ```
- `FromFile(string path)` and `FromStream(Stream stream)` both delegate to `FromText()`, so they get map support automatically.
- `FromFileAsync` and `FromStreamAsync` also call `FromText()` internally.
- `ForEachFromText` and `ForEachFromFile`: When map is set, throw `NotSupportedException("ForEach methods are not supported with fluent maps. Use FromText() instead.")`. Add a test verifying this behavior.

**Writer builder integration:**

In `FixedWidthWriterBuilder<T>`:
- Add field: `private FixedWidthMap<T>? fixedWidthMap;`
- Add `WithMap()` method with AOT annotations
- Extract a helper: `GetRecordWriter(FixedWidthWriteOptions options)`:
  ```csharp
  return fixedWidthMap is not null
      ? FixedWidthRecordWriter<T>.CreateFromTemplates(options, fixedWidthMap.BuildWriteTemplates())
      : FixedWidthRecordWriterFactory.GetWriter<T>(options);
  ```
- Replace factory calls in all terminal methods: `ToText`, `ToFile`, `ToStream`, `ToWriter`, `AppendToFile`, and async variants.

**Integration tests:**
- `WithMap_FromText_ReadsRecords` — read FixedWidth text using map
- `WithMap_FromText_ValidationErrors_Collected` — verify validation errors
- `WithMap_ToText_WritesRecords` — write using map
- `WithMap_RoundTrip` — read then write, verify fidelity
- `InlineMap_FromText_ReadsRecords` — inline `.Map()` on reader builder
- `WithMap_SubclassMap_ReadsRecords` — subclass pattern

- [ ] **Step 1: Write integration tests**
- [ ] **Step 2: Run tests to verify they fail**
- [ ] **Step 3: Add Bind overload to FixedWidthRecordBinder<T>**
- [ ] **Step 4: Add WithMap/Map to reader builder**
- [ ] **Step 5: Add WithMap to writer builder with helper extraction**
- [ ] **Step 6: Run integration tests**
- [ ] **Step 7: Run full test suite**
- [ ] **Step 8: Commit**

```bash
git add src/HeroParser/FixedWidths/Records/Binding/FixedWidthRecordBinder.cs src/HeroParser/FixedWidths/Records/FixedWidthReaderBuilder.cs src/HeroParser/FixedWidths/Writing/FixedWidthWriterBuilder.cs tests/HeroParser.Tests/Mapping/FixedWidthMapIntegrationTests.cs
git commit -m "feat: integrate FixedWidthMap with reader and writer builders"
```

## Chunk 3: Polish and final verification

### Task 14: Run full CI verification

- [ ] **Step 1: Run full test suite across all frameworks**

Run: `dotnet test`
Expected: All tests pass on all frameworks.

- [ ] **Step 2: Check code formatting**

Run: `dotnet format --verify-no-changes`
Expected: No formatting issues.

- [ ] **Step 3: Build in Release mode with warnings-as-errors**

Run: `dotnet build -c Release`
Expected: Clean build, no warnings.

- [ ] **Step 4: Run AOT tests to verify no regressions**

Run: `dotnet run --project tests/HeroParser.AotTests -c Release`
Expected: AOT tests pass (our new code has `[RequiresUnreferencedCode]` so it won't be called from AOT tests).

- [ ] **Step 5: Commit any formatting fixes if needed**

### Task 15: Update README

**Files:**
- Modify: `README.md`

Add a "Fluent Mapping" section near the existing documentation, showing:
- Basic `CsvMap<T>` usage (read + write)
- `FixedWidthMap<T>` usage
- Subclass pattern
- Inline `.Map()` convenience
- Validation rules example (with errors collection)
- Note about AOT incompatibility (`[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`)
- Note about byte-based API limitation (map-based reads use char path, no SIMD)
- Note about `ForEach` methods not supported with fluent maps (use `FromText()` instead)

- [ ] **Step 1: Add documentation**
- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add Fluent Mapping section to README"
```
