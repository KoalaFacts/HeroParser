using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Validation;

public class CsvColumnValidationTests
{
    // Helper: parse CSV and return results + errors
    private static (List<ValidatedTransaction> records, IReadOnlyList<ValidationError> errors) ParseTransactions(string csv)
    {
        var reader = Csv.DeserializeRecords<ValidatedTransaction>(csv);
        var records = new List<ValidatedTransaction>();
        foreach (var record in reader)
        {
            records.Add(record);
        }
        return (records, reader.Errors);
    }

    // ──────────────────────────────────────────────
    // Valid data — no errors expected
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidData_ProducesRecords_WithNoErrors()
    {
        var csv = "Id,Amount,Currency,Reference\nTXN001,500.00,USD,AB1234\nTXN002,1000.00,EUR,CD5678";

        var (records, errors) = ParseTransactions(csv);

        Assert.Equal(2, records.Count);
        Assert.Empty(errors);

        Assert.Equal("TXN001", records[0].TransactionId);
        Assert.Equal(500.00m, records[0].Amount);
        Assert.Equal("USD", records[0].Currency);
        Assert.Equal("AB1234", records[0].Reference);

        Assert.Equal("TXN002", records[1].TransactionId);
        Assert.Equal(1000.00m, records[1].Amount);
        Assert.Equal("EUR", records[1].Currency);
        Assert.Equal("CD5678", records[1].Reference);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AllValid_EmptyErrorList_AfterFullIteration()
    {
        var csv = "Id,Amount,Currency,Reference\n" +
                  "TXN001,100.00,GBP,AB1234\n" +
                  "TXN002,50000.00,JPY,XY9999\n" +
                  "TXN003,0.01,USD,MN0001";

        var (records, errors) = ParseTransactions(csv);

        Assert.Equal(3, records.Count);
        Assert.Empty(errors);
    }

    // ──────────────────────────────────────────────
    // NotNull validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_WhenIdIsEmpty_CollectsErrorAndSkipsRow()
    {
        var csv = "Id,Amount,Currency,Reference\n,500.00,USD,AB1234";

        var (records, errors) = ParseTransactions(csv);

        // Row with validation error is excluded from results
        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "NotNull" && e.PropertyName == "TransactionId");
        Assert.Contains(errors, e => e.ColumnName == "Id");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_WhenAmountIsEmpty_CollectsErrorAndSkipsRow()
    {
        // Amount is NotNull=true (decimal)
        var csv = "Id,Amount,Currency,Reference\nTXN001,,USD,AB1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "NotNull" && e.PropertyName == "Amount");
        // Amount has explicit Index = 1, so ColumnName is null in ValidationError (generator emits null for index-based columns)
    }

    // ──────────────────────────────────────────────
    // NotEmpty validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NotEmpty_WhenIdIsWhitespace_CollectsErrorAndSkipsRow()
    {
        var csv = "Id,Amount,Currency,Reference\n   ,500.00,USD,AB1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "NotEmpty" && e.PropertyName == "TransactionId");
        Assert.Contains(errors, e => e.ColumnName == "Id");
    }

    // ──────────────────────────────────────────────
    // MinLength / MaxLength validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void MaxLength_WhenCurrencyExceedsThreeChars_CollectsErrorAndSkipsRow()
    {
        var csv = "Id,Amount,Currency,Reference\nTXN001,500.00,USDD,AB1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "MaxLength" && e.PropertyName == "Currency");

        var error = errors.First(e => e.Rule == "MaxLength" && e.PropertyName == "Currency");
        // Currency has explicit Index = 2, so ColumnName is null (generator emits null for index-based columns)
        Assert.Equal("USDD", error.RawValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MinLength_WhenCurrencyIsTooShort_CollectsErrorAndSkipsRow()
    {
        var csv = "Id,Amount,Currency,Reference\nTXN001,500.00,US,AB1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "MinLength" && e.PropertyName == "Currency");

        var error = errors.First(e => e.Rule == "MinLength" && e.PropertyName == "Currency");
        // Currency has explicit Index = 2, so ColumnName is null (generator emits null for index-based columns)
        Assert.Equal("US", error.RawValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MinLengthAndMaxLength_ExactlyThreeChars_IsValid()
    {
        var csv = "Id,Amount,Currency,Reference\nTXN001,500.00,USD,AB1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Single(records);
        Assert.DoesNotContain(errors, e => e.PropertyName == "Currency");
    }

    // ──────────────────────────────────────────────
    // Range validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_WhenAmountIsNegative_CollectsErrorAndSkipsRow()
    {
        var csv = "Id,Amount,Currency,Reference\nTXN001,-1.00,USD,AB1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "Range" && e.PropertyName == "Amount");

        var error = errors.First(e => e.Rule == "Range" && e.PropertyName == "Amount");
        // Amount has explicit Index = 1, so ColumnName is null (generator emits null for index-based columns)
        Assert.Equal("Amount", error.PropertyName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_WhenAmountExceedsMaximum_CollectsErrorAndSkipsRow()
    {
        var csv = "Id,Amount,Currency,Reference\nTXN001,200000.00,USD,AB1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_WhenAmountIsAtBoundary_IsValid()
    {
        var csv = "Id,Amount,Currency,Reference\nTXN001,0,USD,AB1234\nTXN002,100000,EUR,CD5678";

        var (records, errors) = ParseTransactions(csv);

        Assert.Equal(2, records.Count);
        Assert.DoesNotContain(errors, e => e.Rule == "Range");
    }

    // ──────────────────────────────────────────────
    // Pattern validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Pattern_WhenReferenceDoesNotMatch_CollectsErrorAndSkipsRow()
    {
        // Reference must match ^[A-Z]{2}\d{4}$ — "invalid" doesn't match
        var csv = "Id,Amount,Currency,Reference\nTXN001,500.00,USD,invalid";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "Pattern" && e.PropertyName == "Reference");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Pattern_WhenReferenceMatchesPattern_IsValid()
    {
        // Reference ^[A-Z]{2}\d{4}$ — "AB1234" matches
        var csv = "Id,Amount,Currency,Reference\nTXN001,500.00,USD,AB1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Single(records);
        Assert.DoesNotContain(errors, e => e.Rule == "Pattern");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Pattern_LowercaseLetters_DoNotMatchPattern()
    {
        // Reference ^[A-Z]{2}\d{4}$ — lowercase "ab1234" doesn't match
        var csv = "Id,Amount,Currency,Reference\nTXN001,500.00,USD,ab1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "Pattern" && e.PropertyName == "Reference");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Pattern_WrongDigitCount_DoNotMatchPattern()
    {
        // Reference ^[A-Z]{2}\d{4}$ — "AB123" has only 3 digits
        var csv = "Id,Amount,Currency,Reference\nTXN001,500.00,USD,AB123";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "Pattern" && e.PropertyName == "Reference");
    }

    // ──────────────────────────────────────────────
    // Multiple errors across rows
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void MultipleRows_ErrorsCollectedAcrossAllInvalidRows()
    {
        // Row 1: valid
        // Row 2: Amount out of range (negative)
        // Row 3: Currency too short
        // Row 4: valid
        var csv = "Id,Amount,Currency,Reference\n" +
                  "TXN001,500.00,USD,AB1234\n" +
                  "TXN002,-10.00,USD,AB1234\n" +
                  "TXN003,100.00,US,AB1234\n" +
                  "TXN004,999.99,EUR,CD5678";

        var (records, errors) = ParseTransactions(csv);

        // Only valid rows produce records
        Assert.Equal(2, records.Count);
        Assert.Equal("TXN001", records[0].TransactionId);
        Assert.Equal("TXN004", records[1].TransactionId);

        // Errors from both invalid rows are collected
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
        Assert.Contains(errors, e => e.Rule == "MinLength" && e.PropertyName == "Currency");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Errors_ContainRowNumbers()
    {
        // Header is row 1, data rows start at row 2
        var csv = "Id,Amount,Currency,Reference\n,500.00,USD,AB1234";

        var (_, errors) = ParseTransactions(csv);

        Assert.NotEmpty(errors);
        // All errors should have a row number > 0
        Assert.All(errors, e => Assert.True(e.RowNumber > 0));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Errors_ContainColumnIndex()
    {
        var csv = "Id,Amount,Currency,Reference\nTXN001,-999.00,USD,AB1234";

        var (_, errors) = ParseTransactions(csv);

        Assert.Contains(errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
        var error = errors.First(e => e.Rule == "Range" && e.PropertyName == "Amount");
        // Column index should be non-negative (Amount is the second column, index 1)
        Assert.True(error.ColumnIndex >= 0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Errors_ContainRawValue()
    {
        var csv = "Id,Amount,Currency,Reference\nTXN001,-999.00,USD,AB1234";

        var (_, errors) = ParseTransactions(csv);

        Assert.Contains(errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
        var error = errors.First(e => e.Rule == "Range" && e.PropertyName == "Amount");
        Assert.Equal("-999.00", error.RawValue);
    }

    // ──────────────────────────────────────────────
    // Mixed valid and invalid rows
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void MixedRows_ValidRowsReturnedDespiteErrors()
    {
        var csv = "Id,Amount,Currency,Reference\n" +
                  "TXN001,500.00,USD,AB1234\n" +
                  "BAD002,999999.00,USD,AB1234\n" +
                  "TXN003,250.00,EUR,CD5678";

        var (records, errors) = ParseTransactions(csv);

        Assert.Equal(2, records.Count);
        Assert.Single(errors);
        Assert.Equal("Range", errors[0].Rule);
        Assert.Equal("Amount", errors[0].PropertyName);
    }

    // ──────────────────────────────────────────────
    // Empty/whitespace on non-nullable value types
    // WITH NotNull — soft validation errors
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_WhenDecimalIsWhitespace_CollectsErrorAndSkipsRow()
    {
        // Amount is NotNull=true (decimal) — whitespace should trigger NotNull validation error
        var csv = "Id,Amount,Currency,Reference\nTXN001,   ,USD,AB1234";

        var (records, errors) = ParseTransactions(csv);

        Assert.Empty(records);
        Assert.Contains(errors, e => e.Rule == "NotNull" && e.PropertyName == "Amount");
    }

    // ──────────────────────────────────────────────
    // Empty/whitespace on non-nullable value types
    // WITHOUT NotNull — hard parse errors
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NonRequired_WhenDecimalIsEmpty_ThrowsParseException()
    {
        var csv = "Name,Amount,Count,Active\nAlice,,1,true";

        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.DeserializeRecords<NonRequiredValueTypeRecord>(csv)) { }
        });

        Assert.Contains("Amount", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NonRequired_WhenDecimalIsWhitespace_ThrowsParseException()
    {
        var csv = "Name,Amount,Count,Active\nAlice,   ,1,true";

        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.DeserializeRecords<NonRequiredValueTypeRecord>(csv)) { }
        });

        Assert.Contains("Amount", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NonRequired_WhenIntIsEmpty_ThrowsParseException()
    {
        var csv = "Name,Amount,Count,Active\nAlice,100.00,,true";

        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.DeserializeRecords<NonRequiredValueTypeRecord>(csv)) { }
        });

        Assert.Contains("Count", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NonRequired_WhenIntIsWhitespace_ThrowsParseException()
    {
        var csv = "Name,Amount,Count,Active\nAlice,100.00,   ,true";

        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.DeserializeRecords<NonRequiredValueTypeRecord>(csv)) { }
        });

        Assert.Contains("Count", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NonRequired_WhenBoolIsEmpty_ThrowsParseException()
    {
        var csv = "Name,Amount,Count,Active\nAlice,100.00,1,";

        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.DeserializeRecords<NonRequiredValueTypeRecord>(csv)) { }
        });

        Assert.Contains("Active", ex.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NonRequired_WhenBoolIsWhitespace_ThrowsParseException()
    {
        var csv = "Name,Amount,Count,Active\nAlice,100.00,1,   ";

        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.DeserializeRecords<NonRequiredValueTypeRecord>(csv)) { }
        });

        Assert.Contains("Active", ex.Message);
    }
}
