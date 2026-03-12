using HeroParser.SeparatedValues.Reading.Records;

namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Declares how a CSV column maps to a property or field on a record type.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class CsvColumnAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the zero-based column index to bind to.
    /// </summary>
    /// <remarks>
    /// When not specified, the mapper will use <see cref="Name"/> (if present) or positional
    /// ordering depending on <see cref="CsvRecordOptions.HasHeaderRow"/>.
    /// </remarks>
    public int Index { get; init; } = -1;

    /// <summary>
    /// Gets or sets the column name to bind to when a header row is present.
    /// </summary>
    /// <remarks>
    /// Matching honors <see cref="CsvRecordOptions.CaseSensitiveHeaders"/>. When omitted, the
    /// property or field name is used.
    /// </remarks>
    public string? Name { get; init; }

    /// <summary>
    /// Gets or sets the format string to use when parsing date/time or numeric values.
    /// </summary>
    /// <remarks>
    /// For date/time types, this is passed to DateTime.ParseExact, DateTimeOffset.ParseExact, etc.
    /// For numeric types, this is passed to the TryParse method's NumberStyles.
    /// When omitted, default parsing rules apply.
    /// </remarks>
    public string? Format { get; init; }

    /// <summary>Value must be present (non-null, column must exist).</summary>
    public bool Required { get; init; }

    /// <summary>String value must not be empty or whitespace. Only valid on string properties.</summary>
    public bool NotEmpty { get; init; }

    /// <summary>Maximum string length. -1 means unchecked. Only valid on string properties.</summary>
    public int MaxLength { get; init; } = -1;

    /// <summary>Minimum string length. -1 means unchecked. Only valid on string properties.</summary>
    public int MinLength { get; init; } = -1;

    /// <summary>Minimum numeric value. NaN means unchecked. Only valid on numeric properties.</summary>
    public double RangeMin { get; init; } = double.NaN;

    /// <summary>Maximum numeric value. NaN means unchecked. Only valid on numeric properties.</summary>
    public double RangeMax { get; init; } = double.NaN;

    /// <summary>Regex pattern the string value must match. Only valid on string properties.</summary>
    public string? Pattern { get; init; }

    /// <summary>Regex timeout in milliseconds for Pattern validation. Default 1000ms.</summary>
    public int PatternTimeoutMs { get; init; } = 1000;
}
