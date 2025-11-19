namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents errors that occur while parsing CSV data.
/// </summary>
public class CsvException : Exception
{
    /// <summary>
    /// Gets the error code describing the failure.
    /// </summary>
    public CsvErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets the 1-based row number where the error occurred, or <see langword="null"/> when unknown.
    /// </summary>
    public int? Row { get; }

    /// <summary>
    /// Gets the 1-based column where the error occurred, or <see langword="null"/> when unknown.
    /// </summary>
    public int? Column { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvException"/> class.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    public CsvException(CsvErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvException"/> class for a specific row.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="row">The 1-based row number associated with the error.</param>
    public CsvException(CsvErrorCode errorCode, string message, int row)
        : base($"Row {row}: {message}")
    {
        ErrorCode = errorCode;
        Row = row;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvException"/> class for a specific row and column.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="row">The 1-based row number associated with the error.</param>
    /// <param name="column">The 1-based column associated with the error.</param>
    public CsvException(CsvErrorCode errorCode, string message, int row, int column)
        : base($"Row {row}, Column {column}: {message}")
    {
        ErrorCode = errorCode;
        Row = row;
        Column = column;
    }
}
