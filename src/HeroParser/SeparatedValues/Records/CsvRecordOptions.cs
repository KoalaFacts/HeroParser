using System.Globalization;

namespace HeroParser.SeparatedValues.Records;

/// <summary>
/// Delegate for converting a CSV column value to a typed value.
/// </summary>
/// <typeparam name="T">The target type to convert to.</typeparam>
/// <param name="value">The raw character span containing the column value.</param>
/// <param name="culture">The culture to use for parsing.</param>
/// <param name="format">Optional format string for parsing.</param>
/// <param name="result">The converted value when successful.</param>
/// <returns><see langword="true"/> if conversion succeeded; otherwise <see langword="false"/>.</returns>
public delegate bool CsvTypeConverter<T>(ReadOnlySpan<char> value, CultureInfo culture, string? format, out T? result);

/// <summary>
/// Internal delegate for custom type conversion that works with the binder.
/// </summary>
internal delegate bool InternalCustomConverter(ReadOnlySpan<char> value, CultureInfo culture, string? format, out object? result);

/// <summary>
/// Provides context about a parse error for error handling callbacks.
/// </summary>
public readonly struct CsvParseErrorContext
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
    /// Gets the name of the member (property) being bound.
    /// </summary>
    public string MemberName { get; init; }

    /// <summary>
    /// Gets the target type that the value was being converted to.
    /// </summary>
    public Type TargetType { get; init; }

    /// <summary>
    /// Gets the raw field value that failed to parse.
    /// </summary>
    public string FieldValue { get; init; }
}

/// <summary>
/// Specifies the action to take when a parse error occurs.
/// </summary>
public enum ParseErrorAction
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
    /// Use the default value for the property and continue.
    /// </summary>
    UseDefault
}

/// <summary>
/// Delegate for handling parse errors during record binding.
/// </summary>
/// <param name="context">Context about the parse error.</param>
/// <returns>The action to take in response to the error.</returns>
public delegate ParseErrorAction CsvParseErrorHandler(CsvParseErrorContext context);

/// <summary>
/// Configures how CSV rows are mapped to strongly typed records.
/// </summary>
public sealed record CsvRecordOptions
{
    private Dictionary<Type, InternalCustomConverter>? customConverters;

    /// <summary>
    /// Gets the registered custom type converters.
    /// </summary>
    internal IReadOnlyDictionary<Type, InternalCustomConverter>? CustomConverters => customConverters;

    /// <summary>
    /// Gets or sets a value that indicates whether the CSV includes a header row.
    /// When <see langword="true"/>, the first row is used to resolve column names.
    /// </summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether header name lookup is case-sensitive.
    /// </summary>
    public bool CaseSensitiveHeaders { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether missing columns are tolerated.
    /// When <see langword="false"/>, missing mappings throw <see cref="CsvException"/>.
    /// </summary>
    public bool AllowMissingColumns { get; init; } = false;

    /// <summary>
    /// Gets or sets a list of string values that should be treated as null during parsing.
    /// </summary>
    /// <remarks>
    /// When a field value matches one of these strings (case-sensitive), it will be treated as null.
    /// Common examples include "NULL", "N/A", "NA", "null", empty string, etc.
    /// By default, this is null, meaning no special null value handling is performed.
    /// </remarks>
    public IReadOnlyList<string>? NullValues { get; init; } = null;

    /// <summary>
    /// Gets or sets the culture to use when parsing culture-sensitive values like dates and numbers.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> (the default), <see cref="CultureInfo.InvariantCulture"/> is used.
    /// This affects parsing of numeric types, dates, and other culture-sensitive types.
    /// </remarks>
    public CultureInfo? Culture { get; init; } = null;

    /// <summary>
    /// Gets or sets the number of rows to skip from the start of the CSV data.
    /// </summary>
    /// <remarks>
    /// Use this to skip metadata rows or other non-data content at the beginning of the file.
    /// The header row (if <see cref="HasHeaderRow"/> is true) is expected after the skipped rows.
    /// </remarks>
    public int SkipRows { get; init; } = 0;

    /// <summary>
    /// Gets or sets a value indicating whether to detect and report duplicate header names.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, a <see cref="CsvException"/> is thrown if the header row
    /// contains duplicate column names (according to <see cref="CaseSensitiveHeaders"/>).
    /// This helps catch data quality issues early. Default is <see langword="false"/>.
    /// </remarks>
    public bool DetectDuplicateHeaders { get; init; } = false;

    /// <summary>
    /// Gets or sets a callback to handle parse errors during record binding.
    /// </summary>
    /// <remarks>
    /// When set, this callback is invoked for each parse error, allowing you to:
    /// <list type="bullet">
    ///   <item><description>Log errors for later analysis</description></item>
    ///   <item><description>Skip problematic rows (<see cref="ParseErrorAction.SkipRow"/>)</description></item>
    ///   <item><description>Use default values (<see cref="ParseErrorAction.UseDefault"/>)</description></item>
    ///   <item><description>Throw exceptions (<see cref="ParseErrorAction.Throw"/>, the default)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var errors = new List&lt;string&gt;();
    /// var options = new CsvRecordOptions
    /// {
    ///     OnParseError = ctx =>
    ///     {
    ///         errors.Add($"Row {ctx.Row}: Failed to parse '{ctx.FieldValue}' for {ctx.MemberName}");
    ///         return ParseErrorAction.SkipRow;
    ///     }
    /// };
    /// </code>
    /// </example>
    public CsvParseErrorHandler? OnParseError { get; init; } = null;

    internal StringComparer HeaderComparer => CaseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Gets the effective culture for parsing, defaulting to <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    internal CultureInfo EffectiveCulture => Culture ?? CultureInfo.InvariantCulture;

    /// <summary>
    /// Registers a custom type converter for the specified type.
    /// </summary>
    /// <typeparam name="T">The type to register the converter for.</typeparam>
    /// <param name="converter">The converter delegate that transforms CSV column values to the target type.</param>
    /// <returns>A new <see cref="CsvRecordOptions"/> instance with the converter registered.</returns>
    /// <remarks>
    /// Custom converters take precedence over built-in converters. Use this to handle domain-specific
    /// types like Money, Address, or other value objects that are not natively supported.
    /// <para>
    /// The converter receives the column value, the culture from <see cref="Culture"/>, and the format
    /// string from <see cref="Binding.CsvColumnAttribute.Format"/> if specified.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new CsvRecordOptions()
    ///     .RegisterConverter&lt;Money&gt;((value, culture, format, out result) =>
    ///     {
    ///         if (decimal.TryParse(value, NumberStyles.Currency, culture, out var amount))
    ///         {
    ///             result = new Money(amount);
    ///             return true;
    ///         }
    ///         result = default;
    ///         return false;
    ///     });
    /// </code>
    /// </example>
    public CsvRecordOptions RegisterConverter<T>(CsvTypeConverter<T> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);

        var newConverters = customConverters is not null
            ? new Dictionary<Type, InternalCustomConverter>(customConverters)
            : [];

        // Wrap the typed converter in an internal converter that boxes the result
        newConverters[typeof(T)] = WrapConverter(converter);

        return this with { customConverters = newConverters };
    }

    private static InternalCustomConverter WrapConverter<T>(CsvTypeConverter<T> converter)
    {
        return (value, culture, format, out result) =>
        {
            if (converter(value, culture, format, out var typedResult))
            {
                result = typedResult;
                return true;
            }
            result = null;
            return false;
        };
    }

    /// <summary>
    /// Gets a reusable default instance.
    /// </summary>
    /// <remarks>
    /// Thread-Safety: This is an immutable singleton and is safe to access from multiple threads.
    /// </remarks>
    public static CsvRecordOptions Default { get; } = new();
}
