using HeroParser.SeparatedValues.Core;
using HeroParser.Tests.Fixtures.Excel;
using HeroParser.Tests.Validation;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Integration")]
public class ExcelValidationTests
{
    // ──────────────────────────────────────────────
    // Strict mode — valid data, no errors expected
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictMode_WithValidData_ReturnsAllRecords()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["TXN001", "500.00", "USD", "AB1234"],
            ["TXN002", "1000.00", "EUR", "CD5678"]
        ]);

        var records = HeroParser.Excel.Read<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Strict)
            .FromStream(xlsx);

        Assert.Equal(2, records.Count);
        Assert.Equal("TXN001", records[0].TransactionId);
        Assert.Equal(500.00m, records[0].Amount);
        Assert.Equal("USD", records[0].Currency);
        Assert.Equal("AB1234", records[0].Reference);
        Assert.Equal("TXN002", records[1].TransactionId);
        Assert.Equal(1000.00m, records[1].Amount);
    }

    // ──────────────────────────────────────────────
    // Strict mode — NotNull violation throws
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictMode_WithNotNullViolation_ThrowsValidationException()
    {
        // Amount is empty — violates NotNull = true
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["TXN001", "", "USD", "AB1234"]
        ]);

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Read<ValidatedTransaction>()
                .WithValidationMode(ValidationMode.Strict)
                .FromStream(xlsx));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "NotNull" && e.PropertyName == "Amount");
    }

    [Fact]
    public void StrictMode_DefaultMode_AlsoThrowsOnViolation()
    {
        // Default validation mode is Strict, so no explicit call to WithValidationMode is needed
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["", "500.00", "USD", "AB1234"]
        ]);

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Read<ValidatedTransaction>().FromStream(xlsx));

        Assert.Contains(ex.Errors, e => e.Rule == "NotNull" && e.PropertyName == "TransactionId");
    }

    // ──────────────────────────────────────────────
    // Lenient mode — invalid rows skipped, no throw
    // ──────────────────────────────────────────────

    [Fact]
    public void LenientMode_WithNotNullViolation_SkipsInvalidRows_NoThrow()
    {
        // Row 2: Amount is empty — NotNull violation, row should be skipped
        // Row 3: valid
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["TXN001", "", "USD", "AB1234"],
            ["TXN002", "250.00", "GBP", "CD5678"]
        ]);

        var records = HeroParser.Excel.Read<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal("TXN002", records[0].TransactionId);
        Assert.Equal(250.00m, records[0].Amount);
    }

    [Fact]
    public void LenientMode_AllRowsInvalid_ReturnsEmptyList()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["TXN001", "", "USD", "AB1234"],
            ["TXN002", "", "EUR", "CD5678"]
        ]);

        var records = HeroParser.Excel.Read<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromStream(xlsx);

        Assert.Empty(records);
    }

    [Fact]
    public void LenientMode_MixedRows_OnlyValidRowsReturned()
    {
        // Row 2: valid
        // Row 3: Amount negative — Range violation
        // Row 4: valid
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["TXN001", "500.00", "USD", "AB1234"],
            ["TXN002", "-10.00", "USD", "AB1234"],
            ["TXN003", "999.99", "EUR", "CD5678"]
        ]);

        var records = HeroParser.Excel.Read<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromStream(xlsx);

        Assert.Equal(2, records.Count);
        Assert.Equal("TXN001", records[0].TransactionId);
        Assert.Equal("TXN003", records[1].TransactionId);
    }

    // ──────────────────────────────────────────────
    // NullValues support
    // ──────────────────────────────────────────────

    [Fact]
    public void NullValues_UnrecognizedToken_ThrowsParseException()
    {
        // Without NullValues, "N/A" cannot be parsed as a decimal — CsvException is thrown
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["TXN001", "N/A", "USD", "AB1234"]
        ]);

        Assert.Throws<CsvException>(() =>
            HeroParser.Excel.Read<ValidatedTransaction>()
                .WithValidationMode(ValidationMode.Strict)
                .FromStream(xlsx));
    }

    [Fact]
    public void NullValues_ConfiguredValueSkipsParsingAndValidation()
    {
        // With "N/A" declared as a null value, the cell is treated as absent rather than
        // parsed — no CsvException is thrown, and Amount defaults to 0m.
        // Because the cell is skipped entirely (treated as null), the NotNull validator
        // is also bypassed — the row returns with Amount = 0.
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["TXN001", "N/A", "USD", "AB1234"]
        ]);

        // In Lenient mode the row is included (even though Amount ends up as 0 since the
        // field is treated as absent); no exception is thrown.
        var records = HeroParser.Excel.Read<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Lenient)
            .WithNullValues("N/A")
            .FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal("TXN001", records[0].TransactionId);
        // Amount defaults to 0m when the cell is treated as a null value
        Assert.Equal(0m, records[0].Amount);
    }

    [Fact]
    public void NullValues_OnlyMatchingCellsAreSkipped()
    {
        // "NULL" is configured as a null value — only the matching cell is skipped.
        // Row 1: Amount = "NULL" → treated as absent, Amount defaults to 0m, no parse error.
        // Row 2: Amount = "100.00" → parsed normally.
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["TXN001", "NULL", "USD", "AB1234"],
            ["TXN002", "100.00", "GBP", "CD5678"]
        ]);

        var records = HeroParser.Excel.Read<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Lenient)
            .WithNullValues("NULL")
            .FromStream(xlsx);

        Assert.Equal(2, records.Count);
        Assert.Equal(0m, records[0].Amount);
        Assert.Equal(100.00m, records[1].Amount);
    }

    // ──────────────────────────────────────────────
    // AllowMissingColumns
    // ──────────────────────────────────────────────

    [Fact]
    public void AllowMissingColumns_MissingColumn_DoesNotThrow()
    {
        // "Reference" column is absent — without AllowMissingColumns, reading a row whose
        // resolved column index points beyond the row would throw for required (non-nullable,
        // non-string) columns. Here we verify no exception is thrown when the flag is set.
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Currency", "Reference"],
            ["TXN001", "USD", "AB1234"]
        ]);

        // "Amount" (non-nullable decimal) is missing from the header, which normally throws.
        // AllowMissingColumns suppresses that and leaves Amount at 0m (its default).
        var records = HeroParser.Excel.Read<ValidatedTransaction>()
            .AllowMissingColumns()
            .WithValidationMode(ValidationMode.Lenient)
            .FromStream(xlsx);

        Assert.Single(records);
        Assert.Equal("TXN001", records[0].TransactionId);
        // Amount defaults to 0m when the column is missing
        Assert.Equal(0m, records[0].Amount);
        Assert.Equal("USD", records[0].Currency);
    }

    [Fact]
    public void WithoutAllowMissingColumns_RequiredNonStringColumnMissing_ThrowsCsvException()
    {
        // "Amount" (non-nullable decimal) is absent from the header — the source-generated
        // binder throws a CsvException during header binding when AllowMissingColumns is false.
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Currency", "Reference"],
            ["TXN001", "USD", "AB1234"]
        ]);

        Assert.Throws<CsvException>(() =>
            HeroParser.Excel.Read<ValidatedTransaction>().FromStream(xlsx));
    }
}
