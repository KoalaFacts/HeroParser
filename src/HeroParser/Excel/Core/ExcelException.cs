namespace HeroParser.Excels.Core;

/// <summary>
/// Exception thrown when an error occurs during Excel file processing.
/// </summary>
public sealed class ExcelException : Exception
{
    /// <summary>Initializes a new instance with the specified message.</summary>
    public ExcelException(string message) : base(message) { }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    public ExcelException(string message, Exception innerException) : base(message, innerException) { }
}
