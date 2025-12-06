using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for v1.3 security features:
/// - CSV Injection Protection
/// - Output Size Limits
/// - Named Field Access (CsvHeaderIndex)
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

}
