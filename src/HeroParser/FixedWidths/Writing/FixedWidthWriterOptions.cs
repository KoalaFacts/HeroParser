using System.Globalization;

namespace HeroParser.FixedWidths.Writing;

/// <summary>
/// Provides context about a serialization error for error handling callbacks.
/// </summary>
public readonly struct FixedWidthSerializeErrorContext
{
    /// <summary>
    /// Gets the 1-based row number where the error occurred.
    /// </summary>
    public int Row { get; init; }

    /// <summary>
    /// Gets the 0-based column index where the error occurred.
    /// </summary>
    public int Column { get; init; }

    /// <summary>
    /// Gets the name of the member (property) being serialized.
    /// </summary>
    public string MemberName { get; init; }

    /// <summary>
    /// Gets the source type of the value being converted.
    /// </summary>
    public Type SourceType { get; init; }

    /// <summary>
    /// Gets the value that failed to serialize (may be null).
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Gets the exception that caused the serialization failure (if any).
    /// </summary>
    public Exception? Exception { get; init; }
}

/// <summary>
/// Specifies the action to take when a serialization error occurs.
/// </summary>
public enum FixedWidthSerializeErrorAction
{
    /// <summary>
    /// Throw an exception (default behavior).
    /// </summary>
    Throw,

    /// <summary>
    /// Skip the current row and continue processing.
    /// </summary>
    SkipRow,

    /// <summary>
    /// Write a padded empty value for the field and continue.
    /// </summary>
    WriteEmpty
}

/// <summary>
/// Delegate for handling serialization errors during record writing.
/// </summary>
/// <param name="context">Context about the serialization error.</param>
/// <returns>The action to take in response to the error.</returns>
public delegate FixedWidthSerializeErrorAction FixedWidthSerializeErrorHandler(FixedWidthSerializeErrorContext context);

/// <summary>
/// Specifies how to handle values that exceed the field width.
/// </summary>
public enum OverflowBehavior
{
    /// <summary>
    /// Truncate the value to fit the field width (default).
    /// </summary>
    Truncate,

    /// <summary>
    /// Throw an exception when a value exceeds the field width.
    /// </summary>
    Throw
}

/// <summary>
/// Configures how HeroParser writes fixed-width data.
/// </summary>
public sealed record FixedWidthWriterOptions
{
    /// <summary>
    /// Gets or sets the newline sequence to use between rows (CRLF by default).
    /// </summary>
    public string NewLine { get; init; } = "\r\n";

    /// <summary>
    /// Gets or sets the default padding character for all fields.
    /// Individual fields can override this via <see cref="Records.Binding.FixedWidthColumnAttribute.PadChar"/>.
    /// </summary>
    public char DefaultPadChar { get; init; } = ' ';

    /// <summary>
    /// Gets or sets the default field alignment for all fields.
    /// Individual fields can override this via <see cref="Records.Binding.FixedWidthColumnAttribute.Alignment"/>.
    /// </summary>
    public FieldAlignment DefaultAlignment { get; init; } = FieldAlignment.Left;

    /// <summary>
    /// Gets or sets the culture used for formatting values (invariant culture by default).
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Gets or sets the string to write for null values (empty/padded by default).
    /// </summary>
    public string NullValue { get; init; } = "";

    /// <summary>
    /// Gets or sets the format string for DateTime values.
    /// </summary>
    /// <remarks>
    /// When null, uses the culture's default DateTime format.
    /// Example: "yyyyMMdd" or "yyyy-MM-dd HH:mm:ss".
    /// </remarks>
    public string? DateTimeFormat { get; init; }

    /// <summary>
    /// Gets or sets the format string for DateOnly values.
    /// </summary>
    /// <remarks>
    /// When null, uses the culture's default date format.
    /// Example: "yyyyMMdd".
    /// </remarks>
    public string? DateOnlyFormat { get; init; }

    /// <summary>
    /// Gets or sets the format string for TimeOnly values.
    /// </summary>
    /// <remarks>
    /// When null, uses the culture's default time format.
    /// Example: "HHmmss".
    /// </remarks>
    public string? TimeOnlyFormat { get; init; }

    /// <summary>
    /// Gets or sets the format string for numeric values.
    /// </summary>
    /// <remarks>
    /// When null, uses the default numeric formatting.
    /// Example: "N2" for 2 decimal places.
    /// </remarks>
    public string? NumberFormat { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of rows to write (DoS protection).
    /// </summary>
    /// <remarks>
    /// When set to a positive value, writing will stop and throw <see cref="FixedWidthException"/>
    /// after the specified number of rows. Set to <see langword="null"/> (the default)
    /// to disable this protection.
    /// </remarks>
    public int? MaxRowCount { get; init; }

    /// <summary>
    /// Gets or sets how to handle values that exceed the field width.
    /// </summary>
    public OverflowBehavior OverflowBehavior { get; init; } = OverflowBehavior.Truncate;

    /// <summary>
    /// Gets or sets a callback to handle serialization errors during record writing.
    /// </summary>
    public FixedWidthSerializeErrorHandler? OnSerializeError { get; init; }

    /// <summary>
    /// Gets or sets the maximum total output size in characters (DoS protection).
    /// </summary>
    /// <remarks>
    /// When set to a positive value, writing will stop and throw <see cref="FixedWidthException"/>
    /// after the specified size is exceeded. Set to <see langword="null"/> (the default)
    /// to disable this protection.
    /// </remarks>
    public long? MaxOutputSize { get; init; }

    /// <summary>
    /// Gets a singleton representing the default configuration.
    /// </summary>
    public static FixedWidthWriterOptions Default { get; } = new();

    /// <summary>
    /// Validates the option set and throws when an invalid value is detected.
    /// </summary>
    /// <exception cref="FixedWidthException">Thrown when any property falls outside the supported range.</exception>
    internal void Validate()
    {
        if (string.IsNullOrEmpty(NewLine))
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                "NewLine cannot be null or empty");
        }

        // Validate NewLine contains only CR/LF characters
        foreach (var c in NewLine)
        {
            if (c != '\r' && c != '\n')
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    $"NewLine must contain only CR (\\r) and LF (\\n) characters, got '{c}' (U+{(int)c:X4})");
            }
        }

        if (MaxRowCount.HasValue && MaxRowCount.Value <= 0)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                $"MaxRowCount must be positive when specified, got {MaxRowCount.Value}");
        }

        if (MaxOutputSize.HasValue && MaxOutputSize.Value <= 0)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                $"MaxOutputSize must be positive when specified, got {MaxOutputSize.Value}");
        }
    }
}
