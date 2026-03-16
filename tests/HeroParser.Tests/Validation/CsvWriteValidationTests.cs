using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Validation;

public class CsvWriteValidationTests
{
    // ──────────────────────────────────────────────
    // Valid data — no exception thrown
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidRecords_WriteSuccessfully()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
            new ValidatedTransaction { TransactionId = "TXN002", Amount = 1000.00m, Currency = "EUR", Reference = "CD5678" }
        };

        var csv = Csv.WriteToText(records);

        Assert.Contains("TXN001", csv);
        Assert.Contains("TXN002", csv);
        Assert.Contains("500", csv);
        Assert.Contains("1000", csv);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidRecord_AtRangeBoundaries_WritesSuccessfully()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 0m, Currency = "USD", Reference = "AB1234" },
            new ValidatedTransaction { TransactionId = "TXN002", Amount = 100000m, Currency = "EUR", Reference = "CD5678" }
        };

        var csv = Csv.WriteToText(records);

        Assert.Contains("TXN001", csv);
        Assert.Contains("TXN002", csv);
    }

    // ──────────────────────────────────────────────
    // NotNull violation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void NotNull_WhenTransactionIdIsEmpty_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "", Amount = 500.00m, Currency = "USD", Reference = "AB1234" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "NotNull" && e.PropertyName == "TransactionId");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NotEmpty_WhenTransactionIdIsWhitespace_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "   ", Amount = 500.00m, Currency = "USD", Reference = "AB1234" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "NotEmpty" && e.PropertyName == "TransactionId");
    }

    // ──────────────────────────────────────────────
    // Range violation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_WhenAmountIsNegative_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = -1.00m, Currency = "USD", Reference = "AB1234" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Range_WhenAmountExceedsMaximum_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 200000.00m, Currency = "USD", Reference = "AB1234" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
    }

    // ──────────────────────────────────────────────
    // MaxLength violation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void MaxLength_WhenCurrencyExceedsThreeChars_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 500.00m, Currency = "USDD", Reference = "AB1234" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "MaxLength" && e.PropertyName == "Currency");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MinLength_WhenCurrencyIsTooShort_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 500.00m, Currency = "US", Reference = "AB1234" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "MinLength" && e.PropertyName == "Currency");
    }

    // ──────────────────────────────────────────────
    // Pattern violation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Pattern_WhenReferenceDoesNotMatch_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "invalid" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "Pattern" && e.PropertyName == "Reference");
    }

    // ──────────────────────────────────────────────
    // Strict mode — first invalid record throws immediately
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void StrictMode_InvalidRecordInMiddle_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
            new ValidatedTransaction { TransactionId = "TXN002", Amount = -1.00m,  Currency = "USD", Reference = "AB1234" },
            new ValidatedTransaction { TransactionId = "TXN003", Amount = 250.00m, Currency = "EUR", Reference = "CD5678" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.Contains(ex.Errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
    }

    // ──────────────────────────────────────────────
    // Lenient mode — invalid records are written as-is
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void LenientMode_InvalidRecord_DoesNotThrow()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "", Amount = -1.00m, Currency = "USDD", Reference = "invalid" }
        };

        var options = new CsvWriteOptions { ValidationMode = ValidationMode.Lenient };

        // Should not throw — validation is skipped entirely in Lenient mode
        var csv = Csv.WriteToText(records, options);

        Assert.NotNull(csv);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LenientMode_InvalidRecordIsWritten_NotSkipped()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
            new ValidatedTransaction { TransactionId = "", Amount = -1.00m, Currency = "USDD", Reference = "invalid" }
        };

        var options = new CsvWriteOptions { ValidationMode = ValidationMode.Lenient };

        var csv = Csv.WriteToText(records, options);

        // Both rows are written — lenient mode skips validation, not rows
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Header + 2 data rows
        Assert.Equal(3, lines.Length);
    }

    // ──────────────────────────────────────────────
    // Source-generated writer enforces validation
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void SourceGeneratedWriter_ValidRecord_WritesSuccessfully()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" }
        };

        // Csv.Write<T>() prefers source-generated writer when [GenerateBinder] is applied
        var csv = Csv.Write<ValidatedTransaction>().ToText(records);

        Assert.Contains("TXN001", csv);
        Assert.Contains("500", csv);
        Assert.Contains("USD", csv);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SourceGeneratedWriter_RangeViolation_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = -99.00m, Currency = "USD", Reference = "AB1234" }
        };

        // Csv.Write<T>() prefers source-generated writer when [GenerateBinder] is applied
        var ex = Assert.Throws<ValidationException>(() =>
            Csv.Write<ValidatedTransaction>().ToText(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SourceGeneratedWriter_MaxLengthViolation_ThrowsValidationException()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 100.00m, Currency = "EURO", Reference = "AB1234" }
        };

        var ex = Assert.Throws<ValidationException>(() =>
            Csv.Write<ValidatedTransaction>().ToText(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "MaxLength" && e.PropertyName == "Currency");
    }

    // ──────────────────────────────────────────────
    // ValidationException — error metadata
    // ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidationException_ContainsRowNumber()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
            new ValidatedTransaction { TransactionId = "TXN002", Amount = -5.00m,  Currency = "USD", Reference = "AB1234" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.All(ex.Errors, e => Assert.True(e.RowNumber > 0));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidationException_ContainsPropertyName()
    {
        var records = new[]
        {
            new ValidatedTransaction { TransactionId = "TXN001", Amount = 200000.00m, Currency = "USD", Reference = "AB1234" }
        };

        var ex = Assert.Throws<ValidationException>(() => Csv.WriteToText(records));

        Assert.Contains(ex.Errors, e => e.PropertyName == "Amount");
    }
}
