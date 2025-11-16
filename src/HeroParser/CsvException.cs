namespace HeroParser;

/// <summary>
/// Exception thrown when CSV parsing fails.
/// </summary>
public class CsvException : Exception
{
    /// <summary>
    /// Error code indicating the type of failure.
    /// </summary>
    public CsvErrorCode ErrorCode { get; }

    /// <summary>
    /// Row number where error occurred (1-based), or null if not applicable.
    /// </summary>
    public int? Row { get; }

    /// <summary>
    /// Column number where error occurred (1-based), or null if not applicable.
    /// </summary>
    public int? Column { get; }

    /// <summary>
    /// Creates a new CsvException with the specified error code and message.
    /// </summary>
    public CsvException(CsvErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a new CsvException with the specified error code, message, and row number.
    /// </summary>
    public CsvException(CsvErrorCode errorCode, string message, int row)
        : base($"Row {row}: {message}")
    {
        ErrorCode = errorCode;
        Row = row;
    }

    /// <summary>
    /// Creates a new CsvException with the specified error code, message, row, and column.
    /// </summary>
    public CsvException(CsvErrorCode errorCode, string message, int row, int column)
        : base($"Row {row}, Column {column}: {message}")
    {
        ErrorCode = errorCode;
        Row = row;
        Column = column;
    }
}
