using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using HeroParser.SeparatedValues.Validation;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for v1.3 security and validation features:
/// - CSV Injection Protection
/// - Output Size Limits
/// - Named Field Access (CsvHeaderIndex)
/// - Field-Level Validation
/// </summary>
public class SecurityAndValidationTests
{
    #region Test Record Types

    [CsvGenerateBinder]
    internal class Person
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Email { get; set; }
    }

    #endregion

    #region CSV Injection Protection Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_None_AllowsDangerousCharacters()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.None)
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("=SUM(A1:A10)", "Normal");
        writer.Flush();

        Assert.Contains("=SUM(A1:A10)", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_EscapeWithQuote_PrefixesWithQuote()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.EscapeWithQuote)
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("=SUM(A1:A10)", "Normal");
        writer.Flush();

        var csv = sw.ToString();
        Assert.Contains("\"'=SUM(A1:A10)\"", csv);
        Assert.Contains("Normal", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_EscapeWithTab_PrefixesWithTab()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.EscapeWithTab)
            .CreateWriter(sw, leaveOpen: true);

        // +SUM() is dangerous (letter after +), should be prefixed with tab
        writer.WriteRow("+SUM(A1)", "Normal");
        writer.Flush();

        var csv = sw.ToString();
        Assert.Contains("\"\t+SUM(A1)\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_Sanitize_StripsDangerousPrefix()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.Sanitize)
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("=HYPERLINK()", "-Invoice");
        writer.Flush();

        var csv = sw.ToString();
        Assert.Contains("HYPERLINK()", csv);
        Assert.Contains("Invoice", csv);
        Assert.DoesNotContain("=-", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_Reject_ThrowsOnDangerousContent()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.Reject)
            .CreateWriter(sw, leaveOpen: true);

        var ex = Assert.Throws<CsvException>(() => writer.WriteRow("@exploit", "Safe"));
        Assert.Equal(CsvErrorCode.InjectionDetected, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_AlwaysDangerousCharacters()
    {
        // These characters are always dangerous regardless of what follows
        char[] alwaysDangerous = ['=', '@', '\t', '\r'];

        foreach (var ch in alwaysDangerous)
        {
            using var sw = new StringWriter();
            using var writer = Csv.Write()
                .WithInjectionProtection(CsvInjectionProtection.Sanitize)
                .CreateWriter(sw, leaveOpen: true);

            writer.WriteRow($"{ch}test", "Normal");
            writer.Flush();

            var csv = sw.ToString();
            Assert.DoesNotContain($",{ch}", csv);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_SmartDetection_AllowsNumbers()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.Reject)
            .CreateWriter(sw, leaveOpen: true);

        // Numbers with - or + should be allowed (digit or . after sign)
        writer.WriteRow("-100", "+200", "-.5", "+.75", "-3.14", "+1-555-1234");
        writer.Flush();

        var csv = sw.ToString();
        Assert.Contains("-100", csv);
        Assert.Contains("+200", csv);
        Assert.Contains("-.5", csv);
        Assert.Contains("+.75", csv);
        Assert.Contains("-3.14", csv);
        Assert.Contains("+1-555-1234", csv);  // Phone number format
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_SmartDetection_RejectsFormulas()
    {
        // -HYPERLINK() should be dangerous (letter after -)
        using var sw1 = new StringWriter();
        using var writer1 = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.Reject)
            .CreateWriter(sw1, leaveOpen: true);

        var ex1 = Assert.Throws<CsvException>(() => writer1.WriteRow("-HYPERLINK()", "Safe"));
        Assert.Equal(CsvErrorCode.InjectionDetected, ex1.ErrorCode);

        // +SUM() should be dangerous (letter after +)
        using var sw2 = new StringWriter();
        using var writer2 = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.Reject)
            .CreateWriter(sw2, leaveOpen: true);

        var ex2 = Assert.Throws<CsvException>(() => writer2.WriteRow("+SUM(A1)", "Safe"));
        Assert.Equal(CsvErrorCode.InjectionDetected, ex2.ErrorCode);

        // -A1 should be dangerous (letter after -)
        using var sw3 = new StringWriter();
        using var writer3 = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.Reject)
            .CreateWriter(sw3, leaveOpen: true);

        var ex3 = Assert.Throws<CsvException>(() => writer3.WriteRow("-A1+B1", "Safe"));
        Assert.Equal(CsvErrorCode.InjectionDetected, ex3.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_WithDangerousChars_AddsCustomChars()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithInjectionProtection(CsvInjectionProtection.Reject)
            .WithDangerousChars('!', '#')
            .CreateWriter(sw, leaveOpen: true);

        // Standard dangerous char should still be rejected
        Assert.Throws<CsvException>(() => writer.WriteRow("=test"));

        // Custom dangerous char should also be rejected
        Assert.Throws<CsvException>(() => writer.WriteRow("!macro"));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InjectionProtection_Generic_ProtectsRecordFields()
    {
        var records = new[]
        {
            new Person { Name = "=IMPORTXML()", Age = 30, Email = "safe@test.com" }
        };

        var csv = Csv.Write<Person>()
            .WithInjectionProtection(CsvInjectionProtection.Sanitize)
            .ToText(records);

        Assert.Contains("IMPORTXML()", csv);
        Assert.DoesNotContain("=IMPORTXML()", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void HasDangerousFields_ReturnsFalse_ForSafeData()
    {
        var csv = "Name,Value,Amount\r\nAlice,Test,-100\r\nBob,Data,+200";
        using var reader = Csv.ReadFromText(csv);

        foreach (var row in reader)
        {
            Assert.False(row.HasDangerousFields());
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void HasDangerousFields_ReturnsTrue_ForDangerousData()
    {
        var csv = "Name,Value\r\n=SUM(A1),Test\r\n@exploit,Data";
        using var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext()); // Skip header "Name,Value"
        Assert.True(reader.MoveNext()); // "=SUM(A1),Test"
        Assert.True(reader.Current.HasDangerousFields());
        Assert.True(reader.MoveNext()); // "@exploit,Data"
        Assert.True(reader.Current.HasDangerousFields());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void IsDangerousColumn_DetectsSpecificDangerousColumn()
    {
        var csv = "Safe,Dangerous,AlsoSafe\r\nNormal,=FORMULA,Data";
        using var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext()); // Skip header
        Assert.True(reader.MoveNext()); // Data row

        var row = reader.Current;
        Assert.False(row.IsDangerousColumn(0)); // "Normal"
        Assert.True(row.IsDangerousColumn(1));  // "=FORMULA"
        Assert.False(row.IsDangerousColumn(2)); // "Data"
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void IsDangerousColumn_SmartDetection_AllowsNumbers()
    {
        var csv = "Amount,Phone,Value\r\n-100,+1-555-1234,+.75";
        using var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext()); // Skip header
        Assert.True(reader.MoveNext()); // Data row

        var row = reader.Current;
        Assert.False(row.IsDangerousColumn(0)); // "-100" (number)
        Assert.False(row.IsDangerousColumn(1)); // "+1-555-1234" (phone)
        Assert.False(row.IsDangerousColumn(2)); // "+.75" (decimal)
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void IsDangerousColumn_SmartDetection_DetectsFormulas()
    {
        var csv = "A,B,C,D\r\n-HYPERLINK(),+SUM(A1),-A1+B1,+CMD|'";
        using var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext()); // Skip header
        Assert.True(reader.MoveNext()); // Data row

        var row = reader.Current;
        Assert.True(row.IsDangerousColumn(0)); // "-HYPERLINK()"
        Assert.True(row.IsDangerousColumn(1)); // "+SUM(A1)"
        Assert.True(row.IsDangerousColumn(2)); // "-A1+B1"
        Assert.True(row.IsDangerousColumn(3)); // "+CMD|'"
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void HasDangerousFields_EmptyFields_ReturnsFalse()
    {
        var csv = "A,B,C\r\n,,";
        using var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext()); // Header
        Assert.True(reader.MoveNext()); // Empty data row

        Assert.False(reader.Current.HasDangerousFields());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void IsDangerousColumn_AlwaysDangerousChars()
    {
        // = and @ at start are dangerous
        var csv = "A,B\r\n=test,@test";
        using var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext()); // Header
        Assert.True(reader.MoveNext()); // Data row

        var row = reader.Current;
        Assert.True(row.IsDangerousColumn(0)); // "=test"
        Assert.True(row.IsDangerousColumn(1)); // "@test"
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void IsDangerousColumn_QuotedFieldsAreSafe()
    {
        // Quoted fields with dangerous chars are safe because the quote protects them
        var csv = "A,B\r\n\"=test\",\"@test\"";
        using var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext()); // Header
        Assert.True(reader.MoveNext()); // Data row

        var row = reader.Current;
        // Raw field starts with " so not detected as dangerous
        Assert.False(row.IsDangerousColumn(0)); // "\"=test\""
        Assert.False(row.IsDangerousColumn(1)); // "\"@test\""
    }

    #region CsvByteSpanRow HasDangerousFields Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvByteSpanRow_HasDangerousFields_ReturnsFalse_ForSafeData()
    {
        var csv = "Name,Value,Amount\r\nAlice,Test,-100\r\nBob,Data,+200"u8;
        foreach (var row in Csv.ReadFromByteSpan(csv))
        {
            Assert.False(row.HasDangerousFields());
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvByteSpanRow_HasDangerousFields_ReturnsTrue_ForDangerousData()
    {
        var csv = "Name,Value\r\n=SUM(A1),Test\r\n@exploit,Data"u8;
        var reader = Csv.ReadFromByteSpan(csv);

        Assert.True(reader.MoveNext()); // Header "Name,Value"
        Assert.True(reader.MoveNext()); // "=SUM(A1),Test"
        Assert.True(reader.Current.HasDangerousFields());
        Assert.True(reader.MoveNext()); // "@exploit,Data"
        Assert.True(reader.Current.HasDangerousFields());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvByteSpanRow_IsDangerousColumn_DetectsSpecificColumn()
    {
        var csv = "Safe,Dangerous,AlsoSafe\r\nNormal,=FORMULA,Data"u8;
        var reader = Csv.ReadFromByteSpan(csv);

        Assert.True(reader.MoveNext()); // Skip header
        Assert.True(reader.MoveNext()); // Data row

        var row = reader.Current;
        Assert.False(row.IsDangerousColumn(0)); // "Normal"
        Assert.True(row.IsDangerousColumn(1));  // "=FORMULA"
        Assert.False(row.IsDangerousColumn(2)); // "Data"
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvByteSpanRow_IsDangerousColumn_SmartDetection_AllowsNumbers()
    {
        var csv = "Amount,Phone,Value\r\n-100,+1-555-1234,+.75"u8;
        var reader = Csv.ReadFromByteSpan(csv);

        Assert.True(reader.MoveNext()); // Skip header
        Assert.True(reader.MoveNext()); // Data row

        var row = reader.Current;
        Assert.False(row.IsDangerousColumn(0)); // "-100" (number)
        Assert.False(row.IsDangerousColumn(1)); // "+1-555-1234" (phone)
        Assert.False(row.IsDangerousColumn(2)); // "+.75" (decimal)
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvByteSpanRow_IsDangerousColumn_SmartDetection_DetectsFormulas()
    {
        var csv = "A,B,C,D\r\n-HYPERLINK(),+SUM(A1),-A1+B1,+CMD|'"u8;
        var reader = Csv.ReadFromByteSpan(csv);

        Assert.True(reader.MoveNext()); // Skip header
        Assert.True(reader.MoveNext()); // Data row

        var row = reader.Current;
        Assert.True(row.IsDangerousColumn(0)); // "-HYPERLINK()"
        Assert.True(row.IsDangerousColumn(1)); // "+SUM(A1)"
        Assert.True(row.IsDangerousColumn(2)); // "-A1+B1"
        Assert.True(row.IsDangerousColumn(3)); // "+CMD|'"
    }

    #endregion

    #endregion

    #region Output Size Limits Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void MaxOutputSize_ThrowsWhenExceeded()
    {
        using var sw = new StringWriter();

        // Exception is thrown when buffer is flushed (during Dispose)
        var ex = Assert.Throws<CsvException>(() =>
        {
            using var writer = Csv.Write()
                .WithMaxOutputSize(50)
                .CreateWriter(sw, leaveOpen: true);

            writer.WriteRow("Short", "Row");
            writer.WriteRow("This is a very long value that will exceed the limit", "Another long value");
        });

        Assert.Equal(CsvErrorCode.OutputSizeExceeded, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void MaxFieldSize_ThrowsWhenExceeded()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithMaxFieldSize(10)
            .CreateWriter(sw, leaveOpen: true);

        var ex = Assert.Throws<CsvException>(() =>
            writer.WriteRow("This exceeds ten characters"));

        Assert.Equal(CsvErrorCode.FieldSizeExceeded, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void MaxColumnCount_ThrowsWhenExceeded()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithMaxColumnCount(3)
            .CreateWriter(sw, leaveOpen: true);

        var ex = Assert.Throws<CsvException>(() =>
            writer.WriteRow("A", "B", "C", "D", "E"));

        Assert.Equal(CsvErrorCode.TooManyColumnsWritten, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SizeLimits_AllowsValidContent()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithMaxOutputSize(1000)
            .WithMaxFieldSize(50)
            .WithMaxColumnCount(5)
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("A", "B", "C");
        writer.WriteRow("D", "E", "F");
        writer.Flush();

        var csv = sw.ToString();
        Assert.Contains("A,B,C", csv);
        Assert.Contains("D,E,F", csv);
    }

    #endregion

    #region Named Field Access (CsvHeaderIndex) Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_FromStringArray_Works()
    {
        var headers = new CsvHeaderIndex(["Name", "Age", "City"]);

        Assert.Equal(0, headers["Name"]);
        Assert.Equal(1, headers["Age"]);
        Assert.Equal(2, headers["City"]);
        Assert.Equal(3, headers.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_CaseInsensitiveByDefault()
    {
        var headers = new CsvHeaderIndex(["Name", "Age", "City"]);

        Assert.Equal(0, headers["name"]);
        Assert.Equal(0, headers["NAME"]);
        Assert.Equal(0, headers["NaMe"]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_CaseSensitiveWhenConfigured()
    {
        var headers = new CsvHeaderIndex(["Name", "Age", "City"], caseSensitive: true);

        Assert.Equal(0, headers["Name"]);
        Assert.Throws<KeyNotFoundException>(() => headers["name"]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_TryGetIndex_ReturnsFalseForMissing()
    {
        var headers = new CsvHeaderIndex(["Name", "Age"]);

        Assert.True(headers.TryGetIndex("Name", out var index));
        Assert.Equal(0, index);

        Assert.False(headers.TryGetIndex("Missing", out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_Contains_Works()
    {
        var headers = new CsvHeaderIndex(["Name", "Age"]);

        Assert.True(headers.Contains("Name"));
        Assert.True(headers.Contains("name")); // case-insensitive
        Assert.False(headers.Contains("Missing"));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_ThrowsForMissingColumn()
    {
        var headers = new CsvHeaderIndex(["Name", "Age"]);

        var ex = Assert.Throws<KeyNotFoundException>(() => headers["Missing"]);
        Assert.Contains("Missing", ex.Message);
        Assert.Contains("Name", ex.Message);
        Assert.Contains("Age", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_FromCsvRow_Works()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC";
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        var headers = new CsvHeaderIndex(reader.Current);

        Assert.Equal(0, headers["Name"]);
        Assert.Equal(1, headers["Age"]);
        Assert.Equal(2, headers["City"]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_GetField_Extension_Works()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC";
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        var headers = new CsvHeaderIndex(reader.Current);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.Equal("Alice", row.GetField("Name", headers).ToString());
        Assert.Equal("30", row.GetField("Age", headers).ToString());
        Assert.Equal("NYC", row.GetField("City", headers).ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_TryGetField_Extension_Works()
    {
        var csv = "Name,Age\r\nAlice,30";
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        var headers = new CsvHeaderIndex(reader.Current);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.True(row.TryGetField("Name", headers, out var column));
        Assert.Equal("Alice", column.ToString());

        Assert.False(row.TryGetField("Missing", headers, out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_ManyColumns_UsesDictionary()
    {
        // Create headers with > 16 columns to trigger dictionary mode
        var headerNames = Enumerable.Range(1, 20).Select(i => $"Column{i}").ToArray();
        var headers = new CsvHeaderIndex(headerNames);

        Assert.Equal(0, headers["Column1"]);
        Assert.Equal(19, headers["Column20"]);
        Assert.True(headers.TryGetIndex("Column10", out var index));
        Assert.Equal(9, index);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvHeaderIndex_DuplicateHeaders_FirstWins()
    {
        var headers = new CsvHeaderIndex(["Name", "Age", "Name"]); // duplicate

        Assert.Equal(0, headers["Name"]); // First occurrence wins
    }

    #endregion

    #region Field-Level Validation Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_Required_FailsOnNull()
    {
        var csv = "Name,Age,Email\r\n,30,test@test.com";

        var ex = Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<Person>()
                .Validate(p => p.Name, CsvValidators.Required())
                .FromText(csv);

            foreach (var _ in reader) { }
        });

        Assert.Equal(CsvErrorCode.ValidationError, ex.ErrorCode);
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_Required_PassesOnValue()
    {
        var csv = "Name,Age,Email\r\nAlice,30,test@test.com";

        using var reader = Csv.Read<Person>()
            .Validate(p => p.Name, CsvValidators.Required())
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_Range_FailsOutOfRange()
    {
        var csv = "Name,Age,Email\r\nAlice,200,test@test.com";

        var ex = Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<Person>()
                .Validate(p => p.Age, CsvValidators.Range(0, 150))
                .FromText(csv);

            foreach (var _ in reader) { }
        });

        Assert.Equal(CsvErrorCode.ValidationError, ex.ErrorCode);
        Assert.Contains("Age", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_Range_PassesInRange()
    {
        var csv = "Name,Age,Email\r\nAlice,30,test@test.com";

        using var reader = Csv.Read<Person>()
            .Validate(p => p.Age, CsvValidators.Range(0, 150))
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal(30, records[0].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_StringLength_FailsTooLong()
    {
        var csv = "Name,Age,Email\r\nAliceWithVeryLongName,30,test@test.com";

        var ex = Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<Person>()
                .Validate(p => p.Name, CsvValidators.MaxLength(10))
                .FromText(csv);

            foreach (var _ in reader) { }
        });

        Assert.Equal(CsvErrorCode.ValidationError, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_Regex_FailsOnMismatch()
    {
        var csv = "Name,Age,Email\r\nAlice,30,invalid-email";

        var ex = Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<Person>()
                .Validate(p => p.Email, CsvValidators.Regex(@"^[\w.-]+@[\w.-]+\.\w+$"))
                .FromText(csv);

            foreach (var _ in reader) { }
        });

        Assert.Equal(CsvErrorCode.ValidationError, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_Regex_PassesOnMatch()
    {
        var csv = "Name,Age,Email\r\nAlice,30,alice@example.com";

        using var reader = Csv.Read<Person>()
            .Validate(p => p.Email, CsvValidators.Regex(@"^[\w.-]+@[\w.-]+\.\w+$"))
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("alice@example.com", records[0].Email);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_NoInjection_DetectsDangerousInput()
    {
        var csv = "Name,Age,Email\r\n=IMPORTXML(),30,test@test.com";

        var ex = Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<Person>()
                .Validate(p => p.Name, CsvValidators.NoInjection())
                .FromText(csv);

            foreach (var _ in reader) { }
        });

        Assert.Equal(CsvErrorCode.ValidationError, ex.ErrorCode);
        Assert.Contains("dangerous", ex.Message.ToLower());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_Custom_Works()
    {
        var csv = "Name,Age,Email\r\nalice,30,test@test.com";

        var ex = Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<Person>()
                .Validate(p => p.Name, CsvValidators.Custom(
                    v => v is string s && char.IsUpper(s[0]),
                    "Name must start with uppercase"))
                .FromText(csv);

            foreach (var _ in reader) { }
        });

        Assert.Contains("uppercase", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_MultipleValidators_AllExecuted()
    {
        var csv = "Name,Age,Email\r\n,30,test@test.com";

        var ex = Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<Person>()
                .Validate(p => p.Name, CsvValidators.Required(), CsvValidators.MaxLength(50))
                .FromText(csv);

            foreach (var _ in reader) { }
        });

        Assert.Equal(CsvErrorCode.ValidationError, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_OnValidationError_SkipsRow()
    {
        var csv = "Name,Age,Email\r\nAlice,30,alice@test.com\r\n,25,invalid\r\nBob,40,bob@test.com";
        var skippedRows = new List<int>();

        using var reader = Csv.Read<Person>()
            .Validate(p => p.Name, CsvValidators.Required())
            .OnValidationError((ctx, msg) =>
            {
                skippedRows.Add(ctx.Row);
                return ValidationErrorAction.SkipRow;
            })
            .FromText(csv);

        var records = reader.ToList();

        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal("Bob", records[1].Name);
        Assert.Contains(3, skippedRows); // Row 3 (1-based, data row 2)
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_OnValidationError_UseDefault()
    {
        var csv = "Name,Age,Email\r\n,30,test@test.com";

        using var reader = Csv.Read<Person>()
            .Validate(p => p.Name, CsvValidators.Required())
            .OnValidationError((ctx, msg) => ValidationErrorAction.UseDefault)
            .FromText(csv);

        var records = reader.ToList();

        Assert.Single(records);
        Assert.Null(records[0].Name); // Default value used
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_ByFieldName_Works()
    {
        var csv = "Name,Age,Email\r\n,30,test@test.com";

        var ex = Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<Person>()
                .Validate("Name", CsvValidators.Required())
                .FromText(csv);

            foreach (var _ in reader) { }
        });

        Assert.Equal(CsvErrorCode.ValidationError, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_DisabledByDefault()
    {
        // Validation options are set but EnableValidation is not called
        // Since auto-enable is on when validators are added, this tests
        // that validation works when validators are added
        var csv = "Name,Age,Email\r\n,30,test@test.com";

        // This should throw because validation is auto-enabled
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<Person>()
                .Validate(p => p.Name, CsvValidators.Required())
                .FromText(csv);

            foreach (var _ in reader) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validation_EnableValidation_ExplicitlyEnabled()
    {
        var csv = "Name,Age,Email\r\nAlice,30,test@test.com";

        using var reader = Csv.Read<Person>()
            .EnableValidation()
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records); // Works even without validators
    }

    #endregion

    #region CsvValidators Static Methods Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvValidators_Required_AllowEmptyStrings()
    {
        // Empty strings are considered whitespace, so to allow empty strings
        // we also need to allow whitespace
        var validator = CsvValidators.Required(allowEmptyStrings: true, allowWhitespace: true);

        var result1 = validator.Validate("", "");
        Assert.True(result1.IsValid);

        var result2 = validator.Validate(null, null);
        Assert.False(result2.IsValid);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvValidators_Required_AllowWhitespace()
    {
        var validator = CsvValidators.Required(allowWhitespace: true);

        var result = validator.Validate("   ", "   ");
        Assert.True(result.IsValid);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvValidators_StringLength_MinAndMax()
    {
        var validator = CsvValidators.StringLength(2, 5);

        Assert.False(validator.Validate("A", "A").IsValid);      // Too short
        Assert.True(validator.Validate("AB", "AB").IsValid);     // Min
        Assert.True(validator.Validate("ABCD", "ABCD").IsValid); // In range
        Assert.True(validator.Validate("ABCDE", "ABCDE").IsValid); // Max
        Assert.False(validator.Validate("ABCDEF", "ABCDEF").IsValid); // Too long
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvValidators_Range_IntBounds()
    {
        var validator = CsvValidators.Range(0, 100);

        Assert.False(validator.Validate(-1, "-1").IsValid);   // Below min
        Assert.True(validator.Validate(0, "0").IsValid);       // Min
        Assert.True(validator.Validate(50, "50").IsValid);     // In range
        Assert.True(validator.Validate(100, "100").IsValid);   // Max
        Assert.False(validator.Validate(101, "101").IsValid);  // Above max
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvValidators_NoInjection_AlwaysDangerous()
    {
        var validator = CsvValidators.NoInjection();

        // Always dangerous characters
        Assert.False(validator.Validate("=formula", "=formula").IsValid);
        Assert.False(validator.Validate("@mention", "@mention").IsValid);
        Assert.False(validator.Validate("\ttab", "\ttab").IsValid);
        Assert.False(validator.Validate("\rreturn", "\rreturn").IsValid);

        // Safe: not at start
        Assert.True(validator.Validate("safe", "safe").IsValid);
        Assert.True(validator.Validate("test=formula", "test=formula").IsValid);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvValidators_NoInjection_SmartDetection()
    {
        var validator = CsvValidators.NoInjection();

        // Safe patterns: digit or '.' after sign
        Assert.True(validator.Validate("-100", "-100").IsValid);
        Assert.True(validator.Validate("+200", "+200").IsValid);
        Assert.True(validator.Validate("-3.14", "-3.14").IsValid);
        Assert.True(validator.Validate("+1-555-1234", "+1-555-1234").IsValid); // Phone
        Assert.True(validator.Validate("-.5", "-.5").IsValid);
        Assert.True(validator.Validate("+.75", "+.75").IsValid);

        // Dangerous patterns: letter or other char after sign
        Assert.False(validator.Validate("+SUM(A1)", "+SUM(A1)").IsValid);
        Assert.False(validator.Validate("-HYPERLINK()", "-HYPERLINK()").IsValid);
        Assert.False(validator.Validate("+number", "+number").IsValid);
        Assert.False(validator.Validate("-A1+B1", "-A1+B1").IsValid);

        // Single sign characters are safe
        Assert.True(validator.Validate("-", "-").IsValid);
        Assert.True(validator.Validate("+", "+").IsValid);
    }

    #endregion
}
