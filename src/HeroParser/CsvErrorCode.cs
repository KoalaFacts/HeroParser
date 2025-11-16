namespace HeroParser;

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
    ParseError = 99
}
