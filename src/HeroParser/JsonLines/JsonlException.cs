namespace HeroParser.JsonLines;

/// <summary>
/// Represents errors that occur while reading or writing JSONL (JSON Lines) data.
/// </summary>
public class JsonlException : Exception
{
    /// <summary>
    /// Gets the error code describing the failure.
    /// </summary>
    public JsonlErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets the 1-based source line number associated with the error, or <see langword="null"/> when unknown.
    /// </summary>
    public long? LineNumber { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonlException"/> class.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    public JsonlException(JsonlErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonlException"/> class for a specific source line.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="lineNumber">The 1-based source line number.</param>
    public JsonlException(JsonlErrorCode errorCode, string message, long lineNumber)
        : base($"Line {lineNumber}: {message}")
    {
        ErrorCode = errorCode;
        LineNumber = lineNumber;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonlException"/> class with an inner exception.
    /// </summary>
    /// <param name="errorCode">The error classification.</param>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="lineNumber">The 1-based source line number.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JsonlException(JsonlErrorCode errorCode, string message, long lineNumber, Exception innerException)
        : base($"Line {lineNumber}: {message}", innerException)
    {
        ErrorCode = errorCode;
        LineNumber = lineNumber;
    }
}
