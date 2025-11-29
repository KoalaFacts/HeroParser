namespace HeroParser.FixedWidths;

/// <summary>
/// Represents errors that occur while parsing fixed-width data.
/// </summary>
public class FixedWidthException : Exception
{
    private const int MAX_FIELD_VALUE_LENGTH = 100;

    /// <summary>
    /// Gets the error code describing the failure.
    /// </summary>
    public FixedWidthErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets the 1-based record number where the error occurred, or <see langword="null"/> when unknown.
    /// </summary>
    public int? Record { get; }

    /// <summary>
    /// Gets the 1-based source line number where the error occurred, or <see langword="null"/> when unknown.
    /// </summary>
    public int? SourceLineNumber { get; }

    /// <summary>
    /// Gets the field start position that caused the error, or <see langword="null"/> when not applicable.
    /// </summary>
    public int? FieldStart { get; }

    /// <summary>
    /// Gets the field length that caused the error, or <see langword="null"/> when not applicable.
    /// </summary>
    public int? FieldLength { get; }

    /// <summary>
    /// Gets the field value that caused the error, or <see langword="null"/> when not applicable.
    /// </summary>
    /// <remarks>
    /// The value is truncated to 100 characters if longer, with "..." appended.
    /// This helps with debugging parse errors by showing the problematic content.
    /// </remarks>
    public string? FieldValue { get; }

    /// <summary>
    /// Gets the name of the field/property that caused the error, or <see langword="null"/> when not applicable.
    /// </summary>
    /// <remarks>
    /// This property contains the name of the record property being bound when a parse error occurs.
    /// It helps identify which field in the record type failed to parse.
    /// </remarks>
    public string? FieldName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWidthException"/> class.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    public FixedWidthException(FixedWidthErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWidthException"/> class for a specific record.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="record">The 1-based record number associated with the error.</param>
    public FixedWidthException(FixedWidthErrorCode errorCode, string message, int record)
        : base($"Record {record}: {message}")
    {
        ErrorCode = errorCode;
        Record = record;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWidthException"/> class with an inner exception.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public FixedWidthException(FixedWidthErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWidthException"/> class with field context.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="record">The 1-based record number associated with the error.</param>
    /// <param name="fieldStart">The 0-based field start position.</param>
    /// <param name="fieldLength">The field length.</param>
    /// <param name="fieldValue">The field value that caused the error.</param>
    public FixedWidthException(FixedWidthErrorCode errorCode, string message, int record, int fieldStart, int fieldLength, string? fieldValue)
        : base(BuildMessageWithFieldValue($"Record {record}, Field [{fieldStart}:{fieldStart + fieldLength}]: {message}", fieldValue))
    {
        ErrorCode = errorCode;
        Record = record;
        FieldStart = fieldStart;
        FieldLength = fieldLength;
        FieldValue = TruncateFieldValue(fieldValue);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWidthException"/> class with source line information.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="record">The 1-based record number associated with the error.</param>
    /// <param name="sourceLineNumber">The 1-based source line number.</param>
    public FixedWidthException(FixedWidthErrorCode errorCode, string message, int record, int sourceLineNumber)
        : base($"Record {record} (Line {sourceLineNumber}): {message}")
    {
        ErrorCode = errorCode;
        Record = record;
        SourceLineNumber = sourceLineNumber;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWidthException"/> class with field name and context.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="record">The 1-based record number associated with the error.</param>
    /// <param name="fieldName">The name of the field/property that caused the error.</param>
    /// <param name="fieldStart">The 0-based field start position.</param>
    /// <param name="fieldLength">The field length.</param>
    /// <param name="fieldValue">The field value that caused the error.</param>
    public FixedWidthException(FixedWidthErrorCode errorCode, string message, int record, string fieldName, int fieldStart, int fieldLength, string? fieldValue)
        : base(BuildMessageWithFieldContext($"Record {record}, Field '{fieldName}' [{fieldStart}:{fieldStart + fieldLength}]: {message}", fieldValue))
    {
        ErrorCode = errorCode;
        Record = record;
        FieldName = fieldName;
        FieldStart = fieldStart;
        FieldLength = fieldLength;
        FieldValue = TruncateFieldValue(fieldValue);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWidthException"/> class with field name, context and source line.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="record">The 1-based record number associated with the error.</param>
    /// <param name="sourceLineNumber">The 1-based source line number.</param>
    /// <param name="fieldName">The name of the field/property that caused the error.</param>
    /// <param name="fieldStart">The 0-based field start position.</param>
    /// <param name="fieldLength">The field length.</param>
    /// <param name="fieldValue">The field value that caused the error.</param>
    public FixedWidthException(FixedWidthErrorCode errorCode, string message, int record, int sourceLineNumber, string fieldName, int fieldStart, int fieldLength, string? fieldValue)
        : base(BuildMessageWithFieldContext($"Record {record} (Line {sourceLineNumber}), Field '{fieldName}' [{fieldStart}:{fieldStart + fieldLength}]: {message}", fieldValue))
    {
        ErrorCode = errorCode;
        Record = record;
        SourceLineNumber = sourceLineNumber;
        FieldName = fieldName;
        FieldStart = fieldStart;
        FieldLength = fieldLength;
        FieldValue = TruncateFieldValue(fieldValue);
    }

    private static string BuildMessageWithFieldContext(string baseMessage, string? fieldValue)
    {
        if (string.IsNullOrEmpty(fieldValue))
            return baseMessage;

        var truncated = TruncateFieldValue(fieldValue);
        return $"{baseMessage} Value: '{truncated}'";
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
