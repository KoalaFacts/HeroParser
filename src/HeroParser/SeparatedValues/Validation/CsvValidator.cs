using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Provides validation for CSV data structure and content.
/// </summary>
/// <remarks>
/// <para>
/// The validator checks for common CSV issues such as:
/// - Missing or incorrect headers
/// - Inconsistent column counts
/// - Parsing errors
/// - Row count limits
/// - Empty files
/// </para>
/// <para>
/// Thread-Safety: All methods are thread-safe as they operate on local state only.
/// </para>
/// </remarks>
public static class CsvValidator
{
    /// <summary>
    /// Validates CSV data according to the specified options.
    /// </summary>
    /// <param name="data">The CSV data to validate.</param>
    /// <param name="options">Validation options. If null, default options are used.</param>
    /// <returns>A validation result containing any errors found.</returns>
    /// <example>
    /// <code>
    /// var options = new CsvValidationOptions
    /// {
    ///     RequiredHeaders = new[] { "Name", "Age", "Email" },
    ///     ExpectedColumnCount = 3
    /// };
    /// var result = CsvValidator.Validate(csvData, options);
    /// if (!result.IsValid)
    /// {
    ///     foreach (var error in result.Errors)
    ///         Console.WriteLine($"Row {error.RowNumber}: {error.Message}");
    /// }
    /// </code>
    /// </example>
    public static CsvValidationResult Validate(string data, CsvValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Validate(data.AsSpan(), options);
    }

    /// <summary>
    /// Validates CSV data according to the specified options.
    /// </summary>
    /// <param name="data">The CSV data to validate (UTF-16).</param>
    /// <param name="options">Validation options. If null, default options are used.</param>
    /// <returns>A validation result containing any errors found.</returns>
    public static CsvValidationResult Validate(ReadOnlySpan<char> data, CsvValidationOptions? options = null)
    {
        options ??= new CsvValidationOptions();
        var errors = new List<CsvValidationError>();

        // Check for empty data
        if (data.IsEmpty || data.Trim().IsEmpty)
        {
            if (!options.AllowEmptyFile)
            {
                errors.Add(new CsvValidationError
                {
                    ErrorType = CsvValidationErrorType.EmptyFile,
                    Message = "CSV file is empty"
                });
            }

            return new CsvValidationResult
            {
                Errors = errors,
                TotalRows = 0,
                ColumnCount = 0,
                Delimiter = options.Delimiter ?? ','
            };
        }

        // Detect delimiter if not specified
        char delimiter = options.Delimiter ?? ',';
        if (!options.Delimiter.HasValue)
        {
            try
            {
                delimiter = CsvDelimiterDetector.DetectDelimiter(data);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(new CsvValidationError
                {
                    ErrorType = CsvValidationErrorType.DelimiterDetectionFailed,
                    Message = $"Could not detect delimiter: {ex.Message}"
                });

                return new CsvValidationResult
                {
                    Errors = errors,
                    TotalRows = 0,
                    ColumnCount = 0,
                    Delimiter = delimiter
                };
            }
        }

        // Create parse options
        var parseOptions = options.GetEffectiveParseOptions();
        if (!options.Delimiter.HasValue)
        {
            parseOptions = parseOptions with { Delimiter = delimiter };
        }

        // Validate the CSV structure
        return ValidateStructure(data, parseOptions, options, errors);
    }

    /// <summary>
    /// Validates UTF-8 encoded CSV data according to the specified options.
    /// </summary>
    /// <param name="data">The CSV data to validate (UTF-8).</param>
    /// <param name="options">Validation options. If null, default options are used.</param>
    /// <returns>A validation result containing any errors found.</returns>
    public static CsvValidationResult Validate(ReadOnlySpan<byte> data, CsvValidationOptions? options = null)
    {
        // Decode UTF-8 to UTF-16 for validation
        var charCount = Encoding.UTF8.GetCharCount(data);
        Span<char> chars = charCount <= 8192
            ? stackalloc char[charCount]
            : new char[charCount];

        Encoding.UTF8.GetChars(data, chars);
        return Validate(chars, options);
    }

    private static CsvValidationResult ValidateStructure(
        ReadOnlySpan<char> data,
        CsvReadOptions parseOptions,
        CsvValidationOptions validationOptions,
        List<CsvValidationError> errors)
    {
        var headers = new List<string>();
        int totalRows = 0;
        int expectedColumnCount = validationOptions.ExpectedColumnCount ?? 0;
        int detectedColumnCount = 0;

        try
        {
            var reader = new CsvRowReader<char>(data, parseOptions);

            // Validate header row if expected
            if (validationOptions.HasHeaderRow && reader.MoveNext())
            {
                totalRows++;
                var headerRow = reader.Current;
                detectedColumnCount = headerRow.ColumnCount;

                // Extract header names
                for (int i = 0; i < headerRow.ColumnCount; i++)
                {
                    headers.Add(headerRow[i].ToString());
                }

                // Check required headers
                if (validationOptions.RequiredHeaders is not null)
                {
                    foreach (var requiredHeader in validationOptions.RequiredHeaders)
                    {
                        if (!headers.Contains(requiredHeader, StringComparer.OrdinalIgnoreCase))
                        {
                            errors.Add(new CsvValidationError
                            {
                                ErrorType = CsvValidationErrorType.MissingHeader,
                                Message = $"Required header '{requiredHeader}' is missing",
                                RowNumber = 1,
                                Expected = requiredHeader
                            });
                        }
                    }
                }

                // Check expected column count
                if (expectedColumnCount > 0 && headerRow.ColumnCount != expectedColumnCount)
                {
                    errors.Add(new CsvValidationError
                    {
                        ErrorType = CsvValidationErrorType.ColumnCountMismatch,
                        Message = $"Header has {headerRow.ColumnCount} columns, expected {expectedColumnCount}",
                        RowNumber = 1,
                        Expected = expectedColumnCount.ToString(),
                        Actual = headerRow.ColumnCount.ToString()
                    });
                }
            }

            // Validate data rows
            while (reader.MoveNext())
            {
                totalRows++;
                var row = reader.Current;

                // Set expected column count from first data row if not specified
                if (detectedColumnCount == 0)
                {
                    detectedColumnCount = row.ColumnCount;
                }

                // Check consistent column count
                if (validationOptions.CheckConsistentColumnCount)
                {
                    var expectedCount = expectedColumnCount > 0 ? expectedColumnCount : detectedColumnCount;
                    if (row.ColumnCount != expectedCount)
                    {
                        errors.Add(new CsvValidationError
                        {
                            ErrorType = CsvValidationErrorType.InconsistentColumnCount,
                            Message = $"Row has {row.ColumnCount} columns, expected {expectedCount}",
                            RowNumber = totalRows,
                            Expected = expectedCount.ToString(),
                            Actual = row.ColumnCount.ToString()
                        });
                    }
                }

                // Check row count limit
                if (totalRows > validationOptions.MaxRows)
                {
                    errors.Add(new CsvValidationError
                    {
                        ErrorType = CsvValidationErrorType.TooManyRows,
                        Message = $"CSV exceeds maximum allowed rows ({validationOptions.MaxRows})",
                        RowNumber = totalRows
                    });
                    break; // Stop validating after limit
                }
            }

            reader.Dispose();
        }
        catch (CsvException ex)
        {
            errors.Add(new CsvValidationError
            {
                ErrorType = CsvValidationErrorType.ParseError,
                Message = $"Parse error: {ex.Message}",
                RowNumber = ex.Row,
                ColumnNumber = ex.Column
            });
        }

        // Check for empty file if no data rows
        if (totalRows == 0 || (totalRows == 1 && validationOptions.HasHeaderRow))
        {
            if (!validationOptions.AllowEmptyFile)
            {
                errors.Add(new CsvValidationError
                {
                    ErrorType = CsvValidationErrorType.EmptyFile,
                    Message = "CSV contains no data rows"
                });
            }
        }

        return new CsvValidationResult
        {
            Errors = errors,
            TotalRows = totalRows,
            ColumnCount = detectedColumnCount,
            Delimiter = parseOptions.Delimiter,
            Headers = headers
        };
    }
}
