namespace HeroParser.FixedWidths;

/// <summary>
/// Error codes for fixed-width file parsing failures.
/// </summary>
/// <remarks>
/// Error code ranges are aligned with <see cref="SeparatedValues.CsvErrorCode"/>:
/// <list type="bullet">
/// <item>1-49: Reader/parsing errors (TooManyRecords, InvalidOptions, etc.)</item>
/// <item>99: General parse error</item>
/// <item>100+: Writer errors (OutputSizeExceeded, FieldOverflow, etc.)</item>
/// </list>
/// </remarks>
public enum FixedWidthErrorCode
{
    // Reader/Parsing errors (1-49)

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

    // Writer errors (100+) - aligned with CsvErrorCode

    /// <summary>
    /// Output size exceeded the maximum allowed limit.
    /// </summary>
    OutputSizeExceeded = 100,

    /// <summary>
    /// Field value exceeds the maximum field width.
    /// </summary>
    FieldOverflow = 101,

    /// <summary>
    /// Too many rows written to the output.
    /// </summary>
    TooManyRowsWritten = 102,

    /// <summary>
    /// Field validation failed during record binding.
    /// </summary>
    ValidationError = 104
}
