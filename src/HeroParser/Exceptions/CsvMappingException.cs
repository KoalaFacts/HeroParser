namespace HeroParser.Exceptions;

/// <summary>
/// Exception thrown when mapping CSV data to strongly-typed objects fails.
/// </summary>
public class CsvMappingException : Exception
{
    /// <summary>
    /// Gets the name of the property that failed to map, if available.
    /// </summary>
    public string? PropertyName { get; }

    /// <summary>
    /// Gets the type that was being mapped to, if available.
    /// </summary>
    public Type? TargetType { get; }

    /// <summary>
    /// Gets the raw value that failed to map, if available.
    /// </summary>
    public string? RawValue { get; }

    /// <summary>
    /// Gets the column index where the mapping error occurred, if available.
    /// </summary>
    public int? ColumnIndex { get; }

    /// <summary>
    /// Gets the line number where the mapping error occurred, if available.
    /// </summary>
    public long? LineNumber { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvMappingException"/> class.
    /// </summary>
    public CsvMappingException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvMappingException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CsvMappingException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvMappingException"/> class with a specified error message and a reference to the inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public CsvMappingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvMappingException"/> class with detailed mapping information.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="propertyName">The name of the property that failed to map.</param>
    /// <param name="targetType">The type that was being mapped to.</param>
    /// <param name="rawValue">The raw value that failed to map.</param>
    /// <param name="columnIndex">The column index where the error occurred.</param>
    /// <param name="lineNumber">The line number where the error occurred.</param>
    public CsvMappingException(string message, string? propertyName, Type? targetType, string? rawValue, int? columnIndex, long? lineNumber) : base(message)
    {
        PropertyName = propertyName;
        TargetType = targetType;
        RawValue = rawValue;
        ColumnIndex = columnIndex;
        LineNumber = lineNumber;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvMappingException"/> class with detailed mapping information and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="propertyName">The name of the property that failed to map.</param>
    /// <param name="targetType">The type that was being mapped to.</param>
    /// <param name="rawValue">The raw value that failed to map.</param>
    /// <param name="columnIndex">The column index where the error occurred.</param>
    /// <param name="lineNumber">The line number where the error occurred.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public CsvMappingException(string message, string? propertyName, Type? targetType, string? rawValue, int? columnIndex, long? lineNumber, Exception innerException) : base(message, innerException)
    {
        PropertyName = propertyName;
        TargetType = targetType;
        RawValue = rawValue;
        ColumnIndex = columnIndex;
        LineNumber = lineNumber;
    }
}