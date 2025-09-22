using System;

namespace HeroParser.Exceptions;

/// <summary>
/// Exception thrown when parsing CSV content fails due to malformed data or parsing errors.
/// </summary>
public class CsvParseException : Exception
{
    /// <summary>
    /// Gets the line number where the parse error occurred, if available.
    /// </summary>
    public long? LineNumber { get; }

    /// <summary>
    /// Gets the position within the line where the parse error occurred, if available.
    /// </summary>
    public int? Position { get; }

    /// <summary>
    /// Gets the raw text content that caused the parse error, if available.
    /// </summary>
    public string? RawContent { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvParseException"/> class.
    /// </summary>
    public CsvParseException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvParseException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CsvParseException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvParseException"/> class with a specified error message and a reference to the inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public CsvParseException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvParseException"/> class with detailed parsing information.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="lineNumber">The line number where the error occurred.</param>
    /// <param name="position">The position within the line where the error occurred.</param>
    /// <param name="rawContent">The raw content that caused the error.</param>
    public CsvParseException(string message, long? lineNumber, int? position, string? rawContent) : base(message)
    {
        LineNumber = lineNumber;
        Position = position;
        RawContent = rawContent;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvParseException"/> class with detailed parsing information and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="lineNumber">The line number where the error occurred.</param>
    /// <param name="position">The position within the line where the error occurred.</param>
    /// <param name="rawContent">The raw content that caused the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public CsvParseException(string message, long? lineNumber, int? position, string? rawContent, Exception innerException) : base(message, innerException)
    {
        LineNumber = lineNumber;
        Position = position;
        RawContent = rawContent;
    }
}