namespace HeroParser.FixedWidths;

/// <summary>
/// Error codes for fixed-width file parsing failures.
/// </summary>
public enum FixedWidthErrorCode
{
    /// <summary>
    /// Record exceeds maximum record limit.
    /// </summary>
    TooManyRecords = 1,

    /// <summary>
    /// Invalid parser options.
    /// </summary>
    InvalidOptions = 2,

    /// <summary>
    /// Record length does not match expected length.
    /// </summary>
    InvalidRecordLength = 3,

    /// <summary>
    /// Field position is out of bounds for the record.
    /// </summary>
    FieldOutOfBounds = 4,

    /// <summary>
    /// General parsing error.
    /// </summary>
    ParseError = 99,

    /// <summary>
    /// Field validation failed during record binding.
    /// </summary>
    ValidationError = 100,

    /// <summary>
    /// Output size exceeded the maximum allowed limit.
    /// </summary>
    OutputSizeExceeded = 200,

    /// <summary>
    /// Field value exceeds the maximum field width.
    /// </summary>
    FieldOverflow = 201,

    /// <summary>
    /// Too many rows written to the output.
    /// </summary>
    TooManyRowsWritten = 202
}
