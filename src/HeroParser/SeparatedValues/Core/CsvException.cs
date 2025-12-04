namespace HeroParser.SeparatedValues.Core;

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
    /// Gets the 1-based logical row number where the error occurred, or <see langword="null"/> when unknown.
    /// </summary>
    /// <remarks>
    /// This represents the ordinal position of the row in the data (1st row, 2nd row, etc.).
    /// Use <see cref="SourceLineNumber"/> for the physical line number in the source file.
    /// </remarks>
    public int? Row { get; }

    /// <summary>
    /// Gets the 1-based column where the error occurred, or <see langword="null"/> when unknown.
    /// </summary>
    public int? Column { get; }

    /// <summary>
    /// Gets the 1-based source line number where the error occurred, or <see langword="null"/> when unknown.
    /// </summary>
    /// <remarks>
    /// This is the physical line number in the source file. For multi-line rows with quoted fields,
    /// this may differ from <see cref="Row"/>. This is useful for debugging and error reporting.
    /// </remarks>
    public int? SourceLineNumber { get; }

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
    /// Creates a new <see cref="CsvException"/> with detailed parsing error information including the inner exception.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="row">The 1-based row number associated with the error.</param>
    /// <param name="column">The 1-based column associated with the error.</param>
    /// <param name="fieldValue">The field value that caused the error.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public CsvException(CsvErrorCode errorCode, string message, int row, int column, string? fieldValue, Exception innerException)
        : base(BuildMessageWithFieldValue($"Row {row}, Column {column}: {message}", fieldValue), innerException)
    {
        ErrorCode = errorCode;
        Row = row;
        Column = column;
        FieldValue = TruncateFieldValue(fieldValue);
    }

    /// <summary>
    /// Private constructor for factory method use.
    /// </summary>
    private CsvException(CsvErrorCode errorCode, string message, int row, int? sourceLineNumber, int? quoteStartPosition, bool _)
        : base(BuildUnterminatedQuoteMessage(row, sourceLineNumber, message, quoteStartPosition))
    {
        ErrorCode = errorCode;
        Row = row;
        SourceLineNumber = sourceLineNumber;
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
        => new(CsvErrorCode.ParseError, message, row, null, quoteStartPosition, true);

    /// <summary>
    /// Creates a new <see cref="CsvException"/> for unterminated quote errors with position and source line information.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="row">The 1-based row number associated with the error.</param>
    /// <param name="sourceLineNumber">The 1-based source line number where the error occurred.</param>
    /// <param name="quoteStartPosition">The 0-based character position where the opening quote was found.</param>
    /// <returns>A new <see cref="CsvException"/> instance.</returns>
    internal static CsvException UnterminatedQuote(string message, int row, int sourceLineNumber, int quoteStartPosition)
        => new(CsvErrorCode.ParseError, message, row, sourceLineNumber, quoteStartPosition, true);

    private static string BuildUnterminatedQuoteMessage(int row, int? sourceLineNumber, string message, int? quoteStartPosition)
    {
        var prefix = sourceLineNumber.HasValue
            ? $"Row {row} (Line {sourceLineNumber.Value})"
            : $"Row {row}";

        return $"{prefix}: {message} (quote started at position {quoteStartPosition})";
    }

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
