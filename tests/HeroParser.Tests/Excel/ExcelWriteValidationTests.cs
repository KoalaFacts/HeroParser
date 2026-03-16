using HeroParser.Tests.Validation;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Excel;

/// <summary>
/// Integration tests for write-side validation in the Excel write API.
/// Covers Strict mode (throws on violation) and Lenient mode (writes without validation).
/// </summary>
[Trait("Category", "Integration")]
public class ExcelWriteValidationTests
{
    // ──────────────────────────────────────────────
    // Strict mode — valid data, no exception
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictMode_ValidRecord_WritesSuccessfully()
    {
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
        };

        var bytes = HeroParser.Excel.Write<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Strict)
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<ValidatedTransaction>().FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("TXN001", readBack[0].TransactionId);
        Assert.Equal(500.00m, readBack[0].Amount);
        Assert.Equal("USD", readBack[0].Currency);
    }

    // ──────────────────────────────────────────────
    // Strict mode — NotNull violation
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictMode_NotNullViolation_ThrowsValidationException()
    {
        // TransactionId has [Validate(NotNull = true, NotEmpty = true)]
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>()
                .WithValidationMode(ValidationMode.Strict)
                .ToBytes(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "NotNull" && e.PropertyName == "TransactionId");
    }

    [Fact]
    public void StrictMode_DefaultValidationMode_AlsoThrowsOnNotNullViolation()
    {
        // Default validation mode is Strict — no explicit call needed
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>().ToBytes(records));

        Assert.Contains(ex.Errors, e => e.Rule == "NotNull" && e.PropertyName == "TransactionId");
    }

    // ──────────────────────────────────────────────
    // Strict mode — Range violation
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictMode_RangeViolation_AmountBelowMin_ThrowsValidationException()
    {
        // Amount has [Validate(RangeMin = 0, RangeMax = 100000)]
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = -1.00m, Currency = "USD", Reference = "AB1234" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>()
                .WithValidationMode(ValidationMode.Strict)
                .ToBytes(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
    }

    [Fact]
    public void StrictMode_RangeViolation_AmountAboveMax_ThrowsValidationException()
    {
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = 200000.00m, Currency = "USD", Reference = "AB1234" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>()
                .WithValidationMode(ValidationMode.Strict)
                .ToBytes(records));

        Assert.Contains(ex.Errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
    }

    // ──────────────────────────────────────────────
    // Strict mode — MaxLength violation
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictMode_MaxLengthViolation_CurrencyTooLong_ThrowsValidationException()
    {
        // Currency has [Validate(MinLength = 3, MaxLength = 3)]
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = 500.00m, Currency = "USDD", Reference = "AB1234" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>()
                .WithValidationMode(ValidationMode.Strict)
                .ToBytes(records));

        Assert.Contains(ex.Errors, e => e.Rule == "MaxLength" && e.PropertyName == "Currency");
    }

    // ──────────────────────────────────────────────
    // Strict mode — second record in sequence triggers
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictMode_InvalidRecordInMiddle_ThrowsValidationException()
    {
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
            new() { TransactionId = "TXN002", Amount = -5.00m, Currency = "USD", Reference = "AB1234" },
            new() { TransactionId = "TXN003", Amount = 250.00m, Currency = "EUR", Reference = "CD5678" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>()
                .WithValidationMode(ValidationMode.Strict)
                .ToBytes(records));

        Assert.Contains(ex.Errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
    }

    // ──────────────────────────────────────────────
    // Lenient mode
    // ──────────────────────────────────────────────

    [Fact]
    public void LenientMode_NotNullViolation_DoesNotThrow()
    {
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
        };

        // Should not throw — validation is skipped entirely in Lenient mode
        var bytes = HeroParser.Excel.Write<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToBytes(records);

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void LenientMode_RangeViolation_DoesNotThrow()
    {
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = -999.00m, Currency = "USD", Reference = "AB1234" },
        };

        var bytes = HeroParser.Excel.Write<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToBytes(records);

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void LenientMode_InvalidRecord_IsWrittenNotSkipped()
    {
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
            new() { TransactionId = "", Amount = -1.00m, Currency = "USDD", Reference = "bad" },
        };

        var bytes = HeroParser.Excel.Write<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        // Both rows are present since Lenient mode does not skip rows
        var rows = HeroParser.Excel.Read().FromStream(ms);
        Assert.Equal(2, rows.Count);
    }

    // ──────────────────────────────────────────────
    // Source-generated writer enforces validation
    // ──────────────────────────────────────────────

    [Fact]
    public void SourceGeneratedWriter_ValidRecord_WritesSuccessfully()
    {
        // HeroParser.Excel.Write<T>() for a [GenerateBinder] type prefers the source-generated writer
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
        };

        var bytes = HeroParser.Excel.Write<ValidatedTransaction>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<ValidatedTransaction>().FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("TXN001", readBack[0].TransactionId);
    }

    [Fact]
    public void SourceGeneratedWriter_NotNullViolation_ThrowsValidationException()
    {
        // ValidatedTransaction has [GenerateBinder] — source-generated path is exercised
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>().ToBytes(records));

        Assert.NotEmpty(ex.Errors);
        Assert.Contains(ex.Errors, e => e.Rule == "NotNull" && e.PropertyName == "TransactionId");
    }

    [Fact]
    public void SourceGeneratedWriter_RangeViolation_ThrowsValidationException()
    {
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = -99.00m, Currency = "USD", Reference = "AB1234" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>().ToBytes(records));

        Assert.Contains(ex.Errors, e => e.Rule == "Range" && e.PropertyName == "Amount");
    }

    // ──────────────────────────────────────────────
    // ValidationException error metadata
    // ──────────────────────────────────────────────

    [Fact]
    public void ValidationException_ContainsPropertyName()
    {
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = 200000.00m, Currency = "USD", Reference = "AB1234" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>().ToBytes(records));

        Assert.Contains(ex.Errors, e => e.PropertyName == "Amount");
    }

    [Fact]
    public void ValidationException_ContainsRowNumber()
    {
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "TXN001", Amount = 500.00m, Currency = "USD", Reference = "AB1234" },
            new() { TransactionId = "TXN002", Amount = -5.00m, Currency = "USD", Reference = "AB1234" },
        };

        var ex = Assert.Throws<ValidationException>(() =>
            HeroParser.Excel.Write<ValidatedTransaction>().ToBytes(records));

        Assert.All(ex.Errors, e => Assert.True(e.RowNumber > 0));
    }
}
