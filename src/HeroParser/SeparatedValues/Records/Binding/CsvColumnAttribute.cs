namespace HeroParser.SeparatedValues.Records.Binding;

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
}
