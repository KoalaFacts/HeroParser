using HeroParser.SeparatedValues.Reading.Records;

namespace HeroParser.SeparatedValues.Reading.Records.Binding;

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
}
