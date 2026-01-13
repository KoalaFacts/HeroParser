using HeroParser.SeparatedValues.Validation;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Validates CSV data structure and content according to the specified options.
    /// </summary>
    /// <param name="data">The CSV data to validate.</param>
    /// <param name="options">Validation options. If null, default options are used.</param>
    /// <returns>A validation result containing any errors found.</returns>
    /// <remarks>
    /// <para>
    /// This method performs structural validation of CSV data without deserializing records.
    /// It checks for issues such as:
    /// - Missing required headers
    /// - Inconsistent column counts
    /// - Parsing errors
    /// - Row count limits
    /// - Empty files
    /// </para>
    /// <para>
    /// Validation is useful for pre-flight checks before processing large CSV files,
    /// especially when handling user-uploaded data or integrating with ETL pipelines.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new CsvValidationOptions
    /// {
    ///     RequiredHeaders = new[] { "Name", "Age", "Email" },
    ///     ExpectedColumnCount = 3,
    ///     MaxRows = 10000
    /// };
    ///
    /// var result = Csv.Validate(csvData, options);
    /// if (!result.IsValid)
    /// {
    ///     Console.WriteLine($"Validation failed with {result.Errors.Count} errors:");
    ///     foreach (var error in result.Errors)
    ///     {
    ///         Console.WriteLine($"  Row {error.RowNumber}: {error.Message}");
    ///     }
    ///     return;
    /// }
    ///
    /// // Proceed with processing validated CSV
    /// var records = Csv.Read&lt;MyRecord&gt;().FromText(csvData);
    /// </code>
    /// </example>
    public static CsvValidationResult Validate(string data, CsvValidationOptions? options = null)
    {
        return CsvValidator.Validate(data, options);
    }

    /// <summary>
    /// Validates CSV data structure and content according to the specified options.
    /// </summary>
    /// <param name="data">The CSV data to validate (UTF-16).</param>
    /// <param name="options">Validation options. If null, default options are used.</param>
    /// <returns>A validation result containing any errors found.</returns>
    public static CsvValidationResult Validate(ReadOnlySpan<char> data, CsvValidationOptions? options = null)
    {
        return CsvValidator.Validate(data, options);
    }

    /// <summary>
    /// Validates UTF-8 encoded CSV data structure and content according to the specified options.
    /// </summary>
    /// <param name="data">The CSV data to validate (UTF-8).</param>
    /// <param name="options">Validation options. If null, default options are used.</param>
    /// <returns>A validation result containing any errors found.</returns>
    public static CsvValidationResult Validate(ReadOnlySpan<byte> data, CsvValidationOptions? options = null)
    {
        return CsvValidator.Validate(data, options);
    }
}
