using System.Globalization;
using HeroParser.Validation;

namespace HeroParser.Excels.Core;

/// <summary>
/// Configures how HeroParser writes Excel (.xlsx) data.
/// </summary>
/// <remarks>
/// Thread-Safety: This is an immutable record type and is safe to share across threads after construction.
/// </remarks>
public sealed record ExcelWriteOptions
{
    /// <summary>
    /// Gets or sets the culture used for formatting values (invariant culture by default).
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Gets or sets the string to write for null values (empty string by default).
    /// </summary>
    public string NullValue { get; init; } = "";

    /// <summary>
    /// Gets or sets the format string for <see cref="DateTime"/> values.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, the value is stored as an OA date serial number with a date style applied.
    /// When set (e.g. <c>"yyyy-MM-dd HH:mm:ss"</c>), the formatted string is stored as a shared string.
    /// </remarks>
    public string? DateTimeFormat { get; init; }

    /// <summary>
    /// Gets or sets the format string for <see cref="DateOnly"/> values.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, the value is stored as an OA date serial number with a date style applied.
    /// When set (e.g. <c>"yyyy-MM-dd"</c>), the formatted string is stored as a shared string.
    /// </remarks>
    public string? DateOnlyFormat { get; init; }

    /// <summary>
    /// Gets or sets the format string for <see cref="TimeOnly"/> values.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, uses the culture's default time format.
    /// Example: <c>"HH:mm:ss"</c>.
    /// </remarks>
    public string? TimeOnlyFormat { get; init; }

    /// <summary>
    /// Gets or sets the format string for numeric values.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, numeric values are written as raw numbers without a format string.
    /// Example: <c>"N2"</c> for two decimal places.
    /// </remarks>
    public string? NumberFormat { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of data rows to write.
    /// </summary>
    /// <remarks>
    /// When set to a positive value, writing stops after the specified number of rows.
    /// Set to <see langword="null"/> (the default) to disable this limit.
    /// </remarks>
    public int? MaxRowCount { get; init; }

    /// <summary>
    /// Gets or sets the validation mode applied when writing records.
    /// </summary>
    /// <remarks>
    /// When set to <see cref="ValidationMode.Strict"/> (the default), any property annotated with
    /// <see cref="ValidateAttribute"/> that fails its rule will cause a <see cref="ValidationException"/>
    /// to be thrown before the row is written.
    /// When set to <see cref="ValidationMode.Lenient"/>, validation is skipped entirely on write.
    /// </remarks>
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Strict;

    /// <summary>
    /// Gets or sets a value indicating whether to write a header row with property names.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool WriteHeader { get; init; } = true;

    /// <summary>
    /// Gets a singleton representing the default configuration.
    /// </summary>
    /// <remarks>
    /// Equivalent to <c>new ExcelWriteOptions()</c>.
    /// Thread-Safety: This is an immutable singleton and is safe to access from multiple threads.
    /// </remarks>
    public static ExcelWriteOptions Default { get; } = new();
}
