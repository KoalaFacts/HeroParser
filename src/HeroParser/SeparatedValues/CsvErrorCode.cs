namespace HeroParser.SeparatedValues;

/// <summary>
/// Error codes for CSV parsing failures.
/// </summary>
public enum CsvErrorCode
{
    /// <summary>
    /// Row exceeds maximum column limit.
    /// </summary>
    TooManyColumns = 1,

    /// <summary>
    /// CSV exceeds maximum row limit.
    /// </summary>
    TooManyRows = 2,

    /// <summary>
    /// Invalid delimiter (must be ASCII 0-127).
    /// </summary>
    InvalidDelimiter = 3,

    /// <summary>
    /// Invalid parser options.
    /// </summary>
    InvalidOptions = 4,

    /// <summary>
    /// General parsing error.
    /// </summary>
    ParseError = 99,

    // Writer error codes (100+)

    /// <summary>
    /// Output exceeds maximum size limit.
    /// </summary>
    OutputSizeExceeded = 100,

    /// <summary>
    /// Field exceeds maximum size limit during write.
    /// </summary>
    FieldSizeExceeded = 101,

    /// <summary>
    /// Row exceeds maximum column count during write.
    /// </summary>
    TooManyColumnsWritten = 102,

    /// <summary>
    /// CSV injection pattern detected and protection mode is Reject.
    /// </summary>
    InjectionDetected = 103,

    /// <summary>
    /// Field validation failed during record binding.
    /// </summary>
    ValidationError = 104
}
