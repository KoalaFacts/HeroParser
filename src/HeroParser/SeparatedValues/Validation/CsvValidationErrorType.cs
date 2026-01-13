namespace HeroParser.SeparatedValues.Validation;

/// <summary>
/// Specifies the type of CSV validation error.
/// </summary>
public enum CsvValidationErrorType
{
    /// <summary>
    /// The CSV structure could not be parsed.
    /// </summary>
    ParseError,

    /// <summary>
    /// A required header is missing.
    /// </summary>
    MissingHeader,

    /// <summary>
    /// The number of columns does not match the expected count.
    /// </summary>
    ColumnCountMismatch,

    /// <summary>
    /// The total number of rows exceeds the maximum allowed.
    /// </summary>
    TooManyRows,

    /// <summary>
    /// The file is empty or contains no data rows.
    /// </summary>
    EmptyFile,

    /// <summary>
    /// A row has inconsistent column count compared to other rows.
    /// </summary>
    InconsistentColumnCount,

    /// <summary>
    /// A required column contains an empty or null value.
    /// </summary>
    RequiredColumnEmpty,

    /// <summary>
    /// The delimiter could not be detected or is ambiguous.
    /// </summary>
    DelimiterDetectionFailed
}
