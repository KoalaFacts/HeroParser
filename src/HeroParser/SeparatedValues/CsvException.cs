namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents errors that occur while parsing CSV data.
/// </summary>
public class CsvException : Exception
{
    private const int MAX_FIELD_VALUE_LENGTH = 100;

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
    /// Gets the field value that caused the error, or <see langword="null"/> when not applicable.
    /// </summary>
    /// <remarks>
    /// The value is truncated to 100 characters if longer, with "..." appended.
    /// This helps with debugging parse errors by showing the problematic content.
    /// </remarks>
    public string? FieldValue { get; }

    /// <summary>
    /// Gets the character position where an unterminated quote started, or <see langword="null"/> when not applicable.
    /// </summary>
    /// <remarks>
    /// This is particularly useful for locating unclosed quotes in large files.
    /// The position is 0-based relative to the start of the row.
    /// </remarks>
    public int? QuoteStartPosition { get; }

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

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvException"/> class with an inner exception.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public CsvException(CsvErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvException"/> class with field value context.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="row">The 1-based row number associated with the error.</param>
    /// <param name="column">The 1-based column associated with the error.</param>
    /// <param name="fieldValue">The field value that caused the error.</param>
    public CsvException(CsvErrorCode errorCode, string message, int row, int column, string? fieldValue)
        : base(BuildMessageWithFieldValue($"Row {row}, Column {column}: {message}", fieldValue))
    {
        ErrorCode = errorCode;
        Row = row;
        Column = column;
        FieldValue = TruncateFieldValue(fieldValue);
    }

    /// <summary>
    /// Private constructor for factory method use.
    /// </summary>
    private CsvException(CsvErrorCode errorCode, string message, int row, int? quoteStartPosition, bool _)
        : base($"Row {row}: {message} (quote started at position {quoteStartPosition})")
    {
        ErrorCode = errorCode;
        Row = row;
        QuoteStartPosition = quoteStartPosition;
    }

    /// <summary>
    /// Creates a new <see cref="CsvException"/> for unterminated quote errors with position information.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="row">The 1-based row number associated with the error.</param>
    /// <param name="quoteStartPosition">The 0-based character position where the opening quote was found.</param>
    /// <returns>A new <see cref="CsvException"/> instance.</returns>
    internal static CsvException UnterminatedQuote(string message, int row, int quoteStartPosition)
        => new(CsvErrorCode.ParseError, message, row, quoteStartPosition, true);

    private static string BuildMessageWithFieldValue(string baseMessage, string? fieldValue)
    {
        if (string.IsNullOrEmpty(fieldValue))
            return baseMessage;

        var truncated = TruncateFieldValue(fieldValue);
        return $"{baseMessage} Value: '{truncated}'";
    }

    private static string? TruncateFieldValue(string? value)
    {
        if (value is null)
            return null;

        if (value.Length <= MAX_FIELD_VALUE_LENGTH)
            return value;

        return value[..MAX_FIELD_VALUE_LENGTH] + "...";
    }
}
