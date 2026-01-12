using System.Globalization;
using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// Provides context about a serialization error for error handling callbacks.
/// </summary>
public readonly struct CsvSerializeErrorContext
{
    /// <summary>
    /// Gets the 1-based row number where the error occurred.
    /// </summary>
    public int Row { get; init; }

    /// <summary>
    /// Gets the 1-based column number where the error occurred.
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
public enum SerializeErrorAction
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
    /// Write the null/empty value for the field and continue.
    /// </summary>
    WriteNull
}

/// <summary>
/// Delegate for handling serialization errors during record writing.
/// </summary>
/// <param name="context">Context about the serialization error.</param>
/// <returns>The action to take in response to the error.</returns>
public delegate SerializeErrorAction CsvSerializeErrorHandler(CsvSerializeErrorContext context);

/// <summary>
/// Configures how HeroParser writes CSV data.
/// </summary>
/// <remarks>
/// <para>
/// The defaults follow RFC 4180. Use <see cref="Validate"/> to catch invalid configurations before writing.
/// </para>
/// <para>
/// Thread-Safety: This is an immutable record type and is safe to share across threads after construction.
/// Call <see cref="Validate"/> once before sharing to ensure configuration validity.
/// </para>
/// </remarks>
public sealed record CsvWriteOptions
{
    /// <summary>
    /// Gets or sets the field delimiter character (comma by default).
    /// </summary>
    /// <remarks>Delimiters must be ASCII (0-127).</remarks>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Gets or sets the quote character used to escape delimiters inside a field (double quote by default).
    /// </summary>
    /// <remarks>The value must be ASCII and cannot match <see cref="Delimiter"/>.</remarks>
    public char Quote { get; init; } = '"';

    /// <summary>
    /// Gets or sets the newline sequence to use between rows (CRLF by default per RFC 4180).
    /// </summary>
    public string NewLine { get; init; } = "\r\n";

    /// <summary>
    /// Gets or sets when fields should be quoted in the output.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="QuoteStyle.WhenNeeded"/> which quotes only when necessary.
    /// </remarks>
    public QuoteStyle QuoteStyle { get; init; } = QuoteStyle.WhenNeeded;

    /// <summary>
    /// Gets or sets a value indicating whether to write a header row with property names.
    /// </summary>
    public bool WriteHeader { get; init; } = true;

    /// <summary>
    /// Gets or sets the culture used for formatting values (invariant culture by default).
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Gets or sets the string to write for null values (empty string by default).
    /// </summary>
    public string NullValue { get; init; } = "";

    /// <summary>
    /// Gets or sets the format string for DateTime values.
    /// </summary>
    /// <remarks>
    /// When null, uses the culture's default DateTime format.
    /// Example: "yyyy-MM-dd HH:mm:ss" or "O" for round-trip format.
    /// </remarks>
    public string? DateTimeFormat { get; init; }

    /// <summary>
    /// Gets or sets the format string for DateOnly values.
    /// </summary>
    /// <remarks>
    /// When null, uses the culture's default date format.
    /// Example: "yyyy-MM-dd".
    /// </remarks>
    public string? DateOnlyFormat { get; init; }

    /// <summary>
    /// Gets or sets the format string for TimeOnly values.
    /// </summary>
    /// <remarks>
    /// When null, uses the culture's default time format.
    /// Example: "HH:mm:ss".
    /// </remarks>
    public string? TimeOnlyFormat { get; init; }

    /// <summary>
    /// Gets or sets the format string for numeric values.
    /// </summary>
    /// <remarks>
    /// When null, uses the default numeric formatting.
    /// Example: "N2" for 2 decimal places, "F4" for fixed point with 4 decimals, "C" for currency.
    /// </remarks>
    public string? NumberFormat { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of rows to write (DoS protection).
    /// </summary>
    /// <remarks>
    /// When set to a positive value, writing will stop and throw <see cref="CsvException"/>
    /// after the specified number of rows. Set to <see langword="null"/> (the default)
    /// to disable this protection.
    /// </remarks>
    public int? MaxRowCount { get; init; }

    /// <summary>
    /// Gets or sets a callback to handle serialization errors during record writing.
    /// </summary>
    /// <remarks>
    /// When set, this callback is invoked for each serialization error, allowing you to:
    /// <list type="bullet">
    ///   <item><description>Log errors for later analysis</description></item>
    ///   <item><description>Skip problematic rows (<see cref="SerializeErrorAction.SkipRow"/>)</description></item>
    ///   <item><description>Write null value (<see cref="SerializeErrorAction.WriteNull"/>)</description></item>
    ///   <item><description>Throw exceptions (<see cref="SerializeErrorAction.Throw"/>, the default)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var errors = new List&lt;string&gt;();
    /// var options = new CsvWriteOptions
    /// {
    ///     OnSerializeError = ctx =>
    ///     {
    ///         errors.Add($"Row {ctx.Row}: Failed to serialize '{ctx.MemberName}'");
    ///         return SerializeErrorAction.WriteNull;
    ///     }
    /// };
    /// </code>
    /// </example>
    public CsvSerializeErrorHandler? OnSerializeError { get; init; } = null;

    /// <summary>
    /// Gets or sets the CSV injection protection mode.
    /// </summary>
    /// <remarks>
    /// <para>CSV injection occurs when user-supplied data begins with characters like <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>,
    /// tab, or carriage return that spreadsheet applications interpret as formulas.</para>
    /// <para>Default is <see cref="CsvInjectionProtection.None"/> for backward compatibility.</para>
    /// </remarks>
    public CsvInjectionProtection InjectionProtection { get; init; } = CsvInjectionProtection.None;

    /// <summary>
    /// Gets or sets additional characters to treat as dangerous beyond the defaults.
    /// </summary>
    /// <remarks>
    /// <para>Default dangerous characters: <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab (<c>\t</c>), carriage return (<c>\r</c>).</para>
    /// <para>Use this property to add custom characters that should trigger injection protection.</para>
    /// </remarks>
    public IReadOnlySet<char>? AdditionalDangerousChars { get; init; }

    /// <summary>
    /// Gets or sets the maximum total output size in characters (DoS protection).
    /// </summary>
    /// <remarks>
    /// When set to a positive value, writing will stop and throw <see cref="CsvException"/>
    /// with <see cref="CsvErrorCode.OutputSizeExceeded"/> after the specified size is exceeded.
    /// Set to <see langword="null"/> (the default) to disable this protection.
    /// </remarks>
    public long? MaxOutputSize { get; init; }

    /// <summary>
    /// Gets or sets the maximum size for a single field in characters (DoS protection).
    /// </summary>
    /// <remarks>
    /// When set to a positive value, writing will throw <see cref="CsvException"/>
    /// with <see cref="CsvErrorCode.FieldSizeExceeded"/> if any field exceeds this size.
    /// Set to <see langword="null"/> (the default) to disable this protection.
    /// </remarks>
    public int? MaxFieldSize { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of columns per row (DoS protection).
    /// </summary>
    /// <remarks>
    /// When set to a positive value, writing will throw <see cref="CsvException"/>
    /// with <see cref="CsvErrorCode.TooManyColumnsWritten"/> if any row exceeds this count.
    /// Set to <see langword="null"/> (the default) to disable this protection.
    /// </remarks>
    public int? MaxColumnCount { get; init; }

    /// <summary>
    /// Gets a singleton representing the default configuration.
    /// </summary>
    /// <remarks>
    /// Equivalent to <c>new CsvWriteOptions()</c>.
    /// Thread-Safety: This is an immutable singleton and is safe to access from multiple threads.
    /// </remarks>
    public static CsvWriteOptions Default { get; } = new();

    /// <summary>
    /// Validates the option set and throws when an invalid value is detected.
    /// </summary>
    /// <exception cref="CsvException">Thrown when any property falls outside the supported range.</exception>
    internal void Validate()
    {
        if (Delimiter > 127)
        {
            throw new CsvException(
                CsvErrorCode.InvalidDelimiter,
                $"Delimiter '{Delimiter}' (U+{(int)Delimiter:X4}) must be ASCII (0-127)");
        }

        if (Quote > 127)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"Quote '{Quote}' (U+{(int)Quote:X4}) must be ASCII (0-127)");
        }

        if (Delimiter == Quote)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"Delimiter and Quote cannot be the same character ('{Delimiter}')");
        }

        if (string.IsNullOrEmpty(NewLine))
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                "NewLine cannot be null or empty");
        }

        // Validate NewLine contains only CR/LF characters
        foreach (var c in NewLine)
        {
            if (c != '\r' && c != '\n')
            {
                throw new CsvException(
                    CsvErrorCode.InvalidOptions,
                    $"NewLine must contain only CR (\\r) and LF (\\n) characters, got '{c}' (U+{(int)c:X4})");
            }
        }

        if (MaxRowCount.HasValue && MaxRowCount.Value <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxRowCount must be positive when specified, got {MaxRowCount.Value}");
        }

        if (MaxOutputSize.HasValue && MaxOutputSize.Value <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxOutputSize must be positive when specified, got {MaxOutputSize.Value}");
        }

        if (MaxFieldSize.HasValue && MaxFieldSize.Value <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxFieldSize must be positive when specified, got {MaxFieldSize.Value}");
        }

        if (MaxColumnCount.HasValue && MaxColumnCount.Value <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxColumnCount must be positive when specified, got {MaxColumnCount.Value}");
        }
    }
}

