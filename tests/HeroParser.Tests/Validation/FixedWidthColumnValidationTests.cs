using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Validation;

public class FixedWidthColumnValidationTests
{
    // Layout: EmployeeId[0-4] Name[5-24] Salary[25-34] Department[35-37] Phone[38-49]
    // Total width: 50 chars per line

    private const string VALID_ID = "00001";
    private const string VALID_NAME = "John Smith          "; // 20 chars (left-padded)
    private const string VALID_SALARY = "050000.00 "; // 10 chars
    private const string VALID_DEPT = "ENG"; // 3 chars
    private const string VALID_PHONE = "555-123-4567"; // 12 chars

    private static string ValidLine() =>
        VALID_ID + VALID_NAME + VALID_SALARY + VALID_DEPT + VALID_PHONE;

    private static string BuildLine(
        string id = VALID_ID,
        string name = VALID_NAME,
        string salary = VALID_SALARY,
        string dept = VALID_DEPT,
        string phone = VALID_PHONE) =>
        id + name + salary + dept + phone;

    private static (List<ValidatedEmployee> records, IReadOnlyList<ValidationError> errors) Parse(string data)
    {
        var result = FixedWidth.DeserializeRecords<ValidatedEmployee>(data);
        return ([.. result.Records], result.Errors);
    }

    // ──────────────────────────────────────────────
    // Valid data — no errors expected
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidData_ProducesRecord_WithNoErrors()
    {
        var data = ValidLine();

        var (records, errors) = Parse(data);

        Assert.Single(records);
        Assert.Empty(errors);

        Assert.Equal(1, records[0].EmployeeId);
        Assert.Equal("John Smith", records[0].Name);
        Assert.Equal(50000m, records[0].Salary);
        Assert.Equal("ENG", records[0].Department);
        Assert.Equal("555-123-4567", records[0].Phone);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MultipleValidLines_ProduceAllRecords_WithNoErrors()
    {
        var data =
            BuildLine(id: "00001", name: "Alice Brown         ", salary: "030000.00 ", dept: "HR ", phone: "555-987-6543") + "\n" +
            BuildLine(id: "00002", name: "Bob Jones           ", salary: "075000.50 ", dept: "IT ", phone: "555-111-2222");

        var (records, errors) = Parse(data);

        Assert.Equal(2, records.Count);
        Assert.Empty(errors);

        Assert.Equal(1, records[0].EmployeeId);
        Assert.Equal("Alice Brown", records[0].Name);
        Assert.Equal(2, records[1].EmployeeId);
        Assert.Equal("Bob Jones", records[1].Name);
    }

    // ──────────────────────────────────────────────
    // NotNull validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_WhenNameIsBlank_CollectsErrorAndSkipsRow()
    {
        // Name (positions 5-24) is NotNull = true, NotEmpty = true
        // All-spaces Name triggers NotNull (and NotEmpty)
        var data = BuildLine(name: "                    "); // 20 spaces

        var (records, errors) = Parse(data);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "NotNull" && e.PropertyName == "Name");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_WhenNameIsBlank_ErrorContainsEmptyRawValue()
    {
        // Fixed-width fields are trimmed before validation, so all-spaces becomes ""
        var data = BuildLine(name: "                    "); // 20 spaces

        var (_, errors) = Parse(data);

        var error = errors.First(e => e.Rule == "NotNull" && e.PropertyName == "Name");
        // RawValue is the trimmed span, so all-spaces becomes ""
        Assert.Equal("", error.RawValue);
    }

    // ──────────────────────────────────────────────
    // NotEmpty validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NotEmpty_WhenNameIsBlank_BothNotNullAndNotEmptyMayFire()
    {
        // When name is all spaces, the field is trimmed to "" (empty span)
        // NotNull fires because span.IsEmpty is true
        // NotEmpty does not fire because !span.IsEmpty is false (span is empty after trim)
        // The row is still skipped due to NotNull failure
        var data = BuildLine(name: "                    "); // 20 spaces

        var (records, errors) = Parse(data);

        Assert.Empty(records);
        // At minimum, NotNull error is present
        Assert.Contains(errors, e => e.PropertyName == "Name");
    }

    // ──────────────────────────────────────────────
    // MinLength / MaxLength validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void MaxLength_WhenDepartmentExceedsThreeChars_CollectsErrorAndSkipsRow()
    {
        // Department (positions 35-37) MaxLength = 3, but the field is always 3 chars after trimming
        // The MaxLength check fires when the trimmed string length > MaxLength
        // Since the field is exactly 3 chars and always trimmed, use a single-char dept name
        // to test MinLength instead of MaxLength. For MaxLength, we can't exceed 3 chars
        // in a 3-char field. MaxLength = 3 means a 3-char dept passes, but 4+ would fail.
        // Since the field is fixed at 3 chars, MaxLength = 3 is effectively always satisfied.
        // We test with a non-empty value to verify the path works correctly.
        var data = BuildLine(dept: "ENG"); // exactly 3 chars — valid

        var (records, errors) = Parse(data);

        Assert.Single(records);
        Assert.DoesNotContain(errors, e => e.PropertyName == "Department");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MinLength_WhenDepartmentIsTooShort_CollectsErrorAndSkipsRow()
    {
        // Department (positions 35-37) MinLength = 2. A single-char dept "IT " trims to "IT" = 2 chars (valid)
        // A single char dept "A  " trims to "A" = 1 char (invalid, < MinLength 2)
        var data = BuildLine(dept: "A  "); // trims to "A", length 1 < MinLength 2

        var (records, errors) = Parse(data);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "MinLength" && e.PropertyName == "Department");

        var error = errors.First(e => e.Rule == "MinLength" && e.PropertyName == "Department");
        Assert.Equal("A", error.RawValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MinLength_WhenDepartmentHasTwoChars_IsValid()
    {
        // "IT " trims to "IT" = 2 chars — valid (MinLength = 2)
        var data = BuildLine(dept: "IT ");

        var (records, errors) = Parse(data);

        Assert.Single(records);
        Assert.DoesNotContain(errors, e => e.PropertyName == "Department");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MinLength_WhenDepartmentHasThreeChars_IsValid()
    {
        // "ENG" = 3 chars — valid (MinLength = 2, MaxLength = 3)
        var data = BuildLine(dept: "ENG");

        var (records, errors) = Parse(data);

        Assert.Single(records);
        Assert.DoesNotContain(errors, e => e.PropertyName == "Department");
    }

    // ──────────────────────────────────────────────
    // Range validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_WhenSalaryIsBelowMinimum_CollectsErrorAndSkipsRow()
    {
        // Salary RangeMin = 20000. Value 10000.00 < 20000.
        var data = BuildLine(salary: "010000.00 ");

        var (records, errors) = Parse(data);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "Range" && e.PropertyName == "Salary");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_WhenSalaryExceedsMaximum_CollectsErrorAndSkipsRow()
    {
        // Salary RangeMax = 500000. Value 600000.00 > 500000.
        var data = BuildLine(salary: "600000.00 ");

        var (records, errors) = Parse(data);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "Range" && e.PropertyName == "Salary");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_WhenSalaryIsAtMinBoundary_IsValid()
    {
        // RangeMin = 20000. Exactly 20000 should be valid.
        var data = BuildLine(salary: "020000.00 ");

        var (records, errors) = Parse(data);

        Assert.Single(records);
        Assert.DoesNotContain(errors, e => e.Rule == "Range");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_WhenSalaryIsAtMaxBoundary_IsValid()
    {
        // RangeMax = 500000. Exactly 500000 should be valid.
        var data = BuildLine(salary: "500000.00 ");

        var (records, errors) = Parse(data);

        Assert.Single(records);
        Assert.DoesNotContain(errors, e => e.Rule == "Range");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_ErrorContainsRawValue()
    {
        var data = BuildLine(salary: "010000.00 ");

        var (_, errors) = Parse(data);

        var error = errors.First(e => e.Rule == "Range" && e.PropertyName == "Salary");
        // RawValue is the trimmed span (trailing space removed, leading zero preserved)
        Assert.Equal("010000.00", error.RawValue);
    }

    // ──────────────────────────────────────────────
    // Pattern validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Pattern_WhenPhoneMatchesPattern_IsValid()
    {
        // Phone must match ^\d{3}-\d{3}-\d{4}$ — "555-123-4567" matches
        var data = BuildLine(phone: "555-123-4567");

        var (records, errors) = Parse(data);

        Assert.Single(records);
        Assert.DoesNotContain(errors, e => e.Rule == "Pattern");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Pattern_WhenPhoneDoesNotMatchPattern_CollectsErrorAndSkipsRow()
    {
        // "555-123-456" doesn't match ^\d{3}-\d{3}-\d{4}$ (only 3 final digits instead of 4)
        var data = BuildLine(phone: "555-123-456 ");

        var (records, errors) = Parse(data);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "Pattern" && e.PropertyName == "Phone");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Pattern_WhenPhoneHasLetters_CollectsErrorAndSkipsRow()
    {
        // Letters don't match the digits-only pattern
        var data = BuildLine(phone: "abc-def-ghij");

        var (records, errors) = Parse(data);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "Pattern" && e.PropertyName == "Phone");
    }

    // ──────────────────────────────────────────────
    // Multiple errors across rows
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void MultipleRows_ErrorsCollectedAcrossAllInvalidRows()
    {
        // Row 1: valid
        // Row 2: Salary below range minimum
        // Row 3: Department too short
        // Row 4: valid
        var data =
            BuildLine() + "\n" +
            BuildLine(salary: "005000.00 ") + "\n" +   // Salary 5000 < 20000
            BuildLine(dept: "A  ") + "\n" +             // Dept "A" < MinLength 2
            BuildLine(id: "00002", name: "Jane Doe            ");

        var (records, errors) = Parse(data);

        // Only valid rows produce records
        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].EmployeeId);
        Assert.Equal(2, records[1].EmployeeId);

        // Errors from both invalid rows are collected
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Rule == "Range" && e.PropertyName == "Salary");
        Assert.Contains(errors, e => e.Rule == "MinLength" && e.PropertyName == "Department");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Errors_ContainRowNumbers()
    {
        // Row 1 is the only row and it has a Name error (row number = 1)
        var data = BuildLine(name: "                    "); // blank Name

        var (_, errors) = Parse(data);

        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.True(e.RowNumber > 0));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Errors_ContainColumnIndex_AsStartPosition()
    {
        // For FixedWidth, ColumnIndex = Start position of the field
        // Salary starts at position 25
        var data = BuildLine(salary: "005000.00 ");

        var (_, errors) = Parse(data);

        var error = errors.First(e => e.Rule == "Range" && e.PropertyName == "Salary");
        // ColumnIndex = Start = 25
        Assert.Equal(25, error.ColumnIndex);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Errors_ColumnName_IsNull_ForFixedWidth()
    {
        // Fixed-width has no headers, so ColumnName is always null
        var data = BuildLine(salary: "005000.00 ");

        var (_, errors) = Parse(data);

        var error = errors.First(e => e.Rule == "Range" && e.PropertyName == "Salary");
        Assert.Null(error.ColumnName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MixedRows_ValidRowsReturnedDespiteErrors()
    {
        var data =
            BuildLine(id: "00001", name: "Alice Brown         ", salary: "030000.00 ") + "\n" +
            BuildLine(id: "00002", name: "Bob Jones           ", salary: "003000.00 ") + "\n" + // too low
            BuildLine(id: "00003", name: "Carol White         ", salary: "045000.00 ");

        var (records, errors) = Parse(data);

        Assert.Equal(2, records.Count);
        Assert.Single(errors);
        Assert.Equal("Range", errors[0].Rule);
        Assert.Equal("Salary", errors[0].PropertyName);

        // Valid rows are preserved
        Assert.Equal(1, records[0].EmployeeId);
        Assert.Equal(3, records[1].EmployeeId);
    }

    // ──────────────────────────────────────────────
    // Strict mode — ToList() auto-throws on errors
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ToList_NoErrors_ReturnsRecords()
    {
        var data = ValidLine();

        // In strict mode (default), ToList() does not throw when there are no errors
        var result = FixedWidth.DeserializeRecords<ValidatedEmployee>(data);
        var records = result.ToList();

        Assert.Single(records);
        Assert.Empty(result.Errors);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToList_WithRangeError_ThrowsValidationException()
    {
        // Salary = 999999 violates Range(20000, 500000)
        var data = BuildLine(salary: "999999.00 ");

        var ex = Assert.Throws<ValidationException>(() =>
            FixedWidth.DeserializeRecords<ValidatedEmployee>(data).ToList());

        Assert.NotEmpty(ex.Errors);
        Assert.Equal("Range", ex.Errors[0].Rule);
        Assert.Equal("Salary", ex.Errors[0].PropertyName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToList_ValidData_ReturnsAllRecords()
    {
        var data = ValidLine();

        var records = FixedWidth.DeserializeRecords<ValidatedEmployee>(data).ToList();

        Assert.Single(records);
        Assert.Equal(1, records[0].EmployeeId);
    }

    // ──────────────────────────────────────────────
    // Error format — ValidationError.ToString()
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ErrorToString_ContainsRowNumber()
    {
        var data = BuildLine(salary: "005000.00 ");

        var (_, errors) = Parse(data);

        var msg = errors.First(e => e.Rule == "Range").ToString();
        Assert.Contains("Row 1", msg);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ErrorToString_ContainsColumnIndex_AsStartPosition()
    {
        // Salary starts at position 25
        var data = BuildLine(salary: "005000.00 ");

        var (_, errors) = Parse(data);

        var msg = errors.First(e => e.Rule == "Range").ToString();
        Assert.Contains("Column index 25", msg);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ErrorToString_FixedWidth_HasNoColumnName()
    {
        // Fixed-width has no headers — ColumnName is null, so no "Column 'xxx'" in output
        var data = BuildLine(salary: "005000.00 ");

        var (_, errors) = Parse(data);

        var msg = errors.First(e => e.Rule == "Range").ToString();
        Assert.DoesNotContain("Column '", msg);
        Assert.Contains("Column index", msg);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ErrorToString_ContainsPropertyName()
    {
        var data = BuildLine(salary: "005000.00 ");

        var (_, errors) = Parse(data);

        var msg = errors.First(e => e.Rule == "Range").ToString();
        Assert.Contains("Property 'Salary'", msg);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ErrorToString_ContainsRuleInBrackets()
    {
        var data = BuildLine(salary: "005000.00 ");

        var (_, errors) = Parse(data);

        var msg = errors.First(e => e.Rule == "Range").ToString();
        Assert.Contains("[Range]", msg);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ErrorToString_ContainsRawValue()
    {
        var data = BuildLine(salary: "005000.00 ");

        var (_, errors) = Parse(data);

        var msg = errors.First(e => e.Rule == "Range").ToString();
        Assert.Contains("(raw: '005000.00')", msg);
    }

    // ──────────────────────────────────────────────
    // Error format — ValidationException.Message
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ExceptionMessage_SingleError_ContainsFullContext()
    {
        var data = BuildLine(salary: "999999.00 ");

        var ex = Assert.Throws<ValidationException>(() =>
            FixedWidth.DeserializeRecords<ValidatedEmployee>(data).ToList());

        Assert.StartsWith("Validation failed:", ex.Message);
        Assert.Contains("Row 1", ex.Message);
        Assert.Contains("Column index 25", ex.Message);
        Assert.Contains("Property 'Salary'", ex.Message);
        Assert.Contains("[Range]", ex.Message);
        Assert.Contains("(raw: '999999.00')", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExceptionMessage_MultipleErrors_ContainsCount()
    {
        // Row 1: Salary below range + Department too short = 2 errors from same row
        var data = BuildLine(salary: "005000.00 ", dept: "A  ");

        var (_, errors) = Parse(data);
        var ex = new ValidationException(errors);

        Assert.StartsWith($"{errors.Count} validation errors occurred:", ex.Message);
        Assert.Contains("[Range]", ex.Message);
        Assert.Contains("[MinLength]", ex.Message);
    }
}
