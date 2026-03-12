using HeroParser.SeparatedValues.Validation;
using Xunit;

namespace HeroParser.Tests;

public class CsvValidationTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_ValidCsv_ReturnsSuccess()
    {
        var csv = "Name,Age,City\nJohn,30,NYC\nJane,25,LA";

        var result = Csv.Validate(csv);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(3, result.TotalRows); // Header + 2 data rows
        Assert.Equal(3, result.ColumnCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_WithRequiredHeaders_ValidatesPresence()
    {
        var csv = "Name,Age,City\nJohn,30,NYC";
        var options = new CsvValidationOptions
        {
            RequiredHeaders = ["Name", "Age", "Email"] // Email is missing
        };

        var result = Csv.Validate(csv, options);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(CsvValidationErrorType.MissingHeader, result.Errors[0].ErrorType);
        Assert.Contains("Email", result.Errors[0].Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_WithExpectedColumnCount_ValidatesCount()
    {
        var csv = "Name,Age,City\nJohn,30,NYC";
        var options = new CsvValidationOptions
        {
            ExpectedColumnCount = 4 // Expecting 4 columns but have 3
        };

        var result = Csv.Validate(csv, options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvValidationErrorType.ColumnCountMismatch);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_InconsistentColumnCount_ReturnsError()
    {
        var csv = "Name,Age,City\nJohn,30,NYC\nJane,25"; // Second data row missing City

        var result = Csv.Validate(csv);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvValidationErrorType.InconsistentColumnCount);
        Assert.Equal(3, result.Errors[0].RowNumber); // Third row (header + 2 data)
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_EmptyFile_ReturnsError()
    {
        var csv = "";

        var result = Csv.Validate(csv);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(CsvValidationErrorType.EmptyFile, result.Errors[0].ErrorType);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_EmptyFile_WithAllowEmptyFile_ReturnsSuccess()
    {
        var csv = "";
        var options = new CsvValidationOptions { AllowEmptyFile = true };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_HeaderOnly_ReturnsEmptyFileError()
    {
        var csv = "Name,Age,City";

        var result = Csv.Validate(csv);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvValidationErrorType.EmptyFile);
        Assert.Contains("no data rows", result.Errors[0].Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_TooManyRows_ReturnsError()
    {
        var csv = "Name,Age\n" + string.Join("\n", Enumerable.Range(1, 150).Select(i => $"Person{i},{i}"));
        var options = new CsvValidationOptions { MaxRows = 100 };

        var result = Csv.Validate(csv, options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvValidationErrorType.TooManyRows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_WithAutoDelimiterDetection_DetectsAndValidates()
    {
        var csv = "Name;Age;City\nJohn;30;NYC\nJane;25;LA";
        var options = new CsvValidationOptions
        {
            Delimiter = null // Auto-detect
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Equal(';', result.Delimiter);
        Assert.Equal(3, result.ColumnCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_WithAutoDelimiterDetection_AmbiguousDelimiter_ReturnsError()
    {
        var csv = "JustSomeTextWithoutDelimiters\nAnotherLine";
        var options = new CsvValidationOptions
        {
            Delimiter = null // Auto-detect
        };

        var result = Csv.Validate(csv, options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvValidationErrorType.DelimiterDetectionFailed);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_NoHeaderRow_ValidatesCorrectly()
    {
        var csv = "John,30,NYC\nJane,25,LA\nBob,35,SF";
        var options = new CsvValidationOptions
        {
            HasHeaderRow = false,
            ExpectedColumnCount = 3
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.TotalRows);
        Assert.Equal(3, result.ColumnCount);
        Assert.Empty(result.Headers);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_WithDisabledConsistencyCheck_AllowsInconsistentColumns()
    {
        var csv = "Name,Age,City\nJohn,30,NYC\nJane,25"; // Second row missing City
        var options = new CsvValidationOptions
        {
            CheckConsistentColumnCount = false
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_ReturnsHeaders_WhenHeaderRowPresent()
    {
        var csv = "Name,Age,Email\nJohn,30,john@example.com";

        var result = Csv.Validate(csv);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Headers.Count);
        Assert.Equal("Name", result.Headers[0]);
        Assert.Equal("Age", result.Headers[1]);
        Assert.Equal("Email", result.Headers[2]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_CaseInsensitiveHeaderCheck_MatchesCorrectly()
    {
        var csv = "name,AGE,city\nJohn,30,NYC";
        var options = new CsvValidationOptions
        {
            RequiredHeaders = ["Name", "Age", "City"] // Different case
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid); // Should match case-insensitively by default
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_WithQuotedFields_ValidatesCorrectly()
    {
        var csv = "Name,Age,Description\n\"Smith, John\",30,\"Engineer, Senior\"\n\"Doe, Jane\",25,Manager";

        var result = Csv.Validate(csv);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.TotalRows);
        Assert.Equal(3, result.ColumnCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_RealWorldScenario_UserUploadedFile()
    {
        var csv = @"First Name,Last Name,Email,Phone
John,Smith,john@example.com,555-1234
Jane,Doe,jane@example.com,555-5678
Bob,Johnson,bob@example.com,555-9012";

        var options = new CsvValidationOptions
        {
            RequiredHeaders = ["First Name", "Last Name", "Email"],
            ExpectedColumnCount = 4,
            MaxRows = 10000
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Equal(4, result.TotalRows);
        Assert.Equal(4, result.ColumnCount);
        Assert.Equal(',', result.Delimiter);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_SkipRows_SkipsMetadataPreamble()
    {
        var csv = "# Report Title\n# Generated 2026-03-12\nName,Age,City\nJohn,30,NYC\nJane,25,LA";
        var options = new CsvValidationOptions
        {
            Delimiter = ',',
            SkipRows = 2,
            RequiredHeaders = ["Name", "Age", "City"]
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Equal(5, result.TotalRows); // 2 skipped + 1 header + 2 data
        Assert.Equal(3, result.ColumnCount);
        Assert.Contains("Name", result.Headers);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_SkipRows_WithoutHeader_SkipsAndValidatesData()
    {
        var csv = "# metadata\nJohn,30,NYC\nJane,25,LA";
        var options = new CsvValidationOptions
        {
            Delimiter = ',',
            SkipRows = 1,
            HasHeaderRow = false,
            ExpectedColumnCount = 3
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.TotalRows); // 1 skipped + 2 data
        Assert.Equal(3, result.ColumnCount);
        Assert.Empty(result.Headers);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_SkipRows_HeaderOnly_NoData_ReportsEmptyFile()
    {
        // 2 metadata rows + 1 header row = 3 rows, but zero data rows
        var csv = "# metadata1\n# metadata2\nName,Age,City";
        var options = new CsvValidationOptions
        {
            Delimiter = ',',
            SkipRows = 2,
            RequiredHeaders = ["Name", "Age", "City"]
        };

        var result = Csv.Validate(csv, options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvValidationErrorType.EmptyFile);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_SkipRows_ExceedsAvailableRows_ReportsEmptyFile()
    {
        var csv = "row1\nrow2";
        var options = new CsvValidationOptions
        {
            Delimiter = ',',
            SkipRows = 10,
            HasHeaderRow = false
        };

        var result = Csv.Validate(csv, options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorType == CsvValidationErrorType.EmptyFile);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_SkipRows_Utf8ByteInput_Works()
    {
        var csv = "# metadata\nName,Age\nJohn,30\nJane,25"u8;
        var options = new CsvValidationOptions
        {
            Delimiter = ',',
            SkipRows = 1,
            RequiredHeaders = ["Name", "Age"]
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Equal(4, result.TotalRows);
        Assert.Contains("Name", result.Headers);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_SkipRows_WithMaxRows_CountsFromStart()
    {
        // 1 skipped + 1 header + 5 data = 7 total. MaxRows = 7 allows all.
        var csv = "# meta\nName,Age\n" + string.Join("\n", Enumerable.Range(1, 5).Select(i => $"Person{i},{i}"));
        var options = new CsvValidationOptions
        {
            Delimiter = ',',
            SkipRows = 1,
            MaxRows = 7
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Equal(7, result.TotalRows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_MultipleErrors_ReturnsAllErrors()
    {
        var csv = "Name,Age\nJohn,30,Extra\nJane"; // Multiple issues
        var options = new CsvValidationOptions
        {
            Delimiter = ',', // Must specify delimiter since inconsistent column counts confuse auto-detection
            RequiredHeaders = ["Name", "Age", "Email"], // Missing Email
            CheckConsistentColumnCount = true
        };

        var result = Csv.Validate(csv, options);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2); // Missing header + inconsistent columns
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_Utf8Data_ValidatesCorrectly()
    {
        var csv = "Name,Age,City\nJohn,30,NYC\nJane,25,LA"u8;

        var result = Csv.Validate(csv);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.TotalRows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_EuropeanCsvFormat_ValidatesWithSemicolon()
    {
        var csv = "Product;Price;Stock\nCoffee;4,50;100\nTea;3,25;150";
        var options = new CsvValidationOptions
        {
            Delimiter = ';'
        };

        var result = Csv.Validate(csv, options);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.TotalRows);
        Assert.Equal(3, result.ColumnCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Validate_ValidationErrorsContainRowAndColumnNumbers()
    {
        var csv = "Name,Age,City\nJohn,30,NYC\nJane,25"; // Missing city on row 3

        var result = Csv.Validate(csv);

        Assert.False(result.IsValid);
        var error = result.Errors.First(e => e.ErrorType == CsvValidationErrorType.InconsistentColumnCount);
        Assert.Equal(3, error.RowNumber);
        Assert.Contains("2", error.Actual); // Actual column count (Jane,25 has 2 columns)
    }
}
