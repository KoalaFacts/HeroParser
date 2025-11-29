using HeroParser.FixedWidths;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Declares how a fixed-width field maps to a property or field on a record type.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class FixedWidthColumnAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the zero-based starting position of the field in the record.
    /// </summary>
    public int Start { get; init; }

    /// <summary>
    /// Gets or sets the length of the field in characters.
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// Gets or sets the padding character to trim from the field.
    /// When not specified, uses <see cref="FixedWidthParserOptions.DefaultPadChar"/>.
    /// </summary>
    public char PadChar { get; init; } = '\0'; // '\0' means "use default"

    /// <summary>
    /// Gets or sets the field alignment, which determines how trimming is applied.
    /// When not specified, uses <see cref="FixedWidthParserOptions.DefaultAlignment"/>.
    /// </summary>
    public FieldAlignment Alignment { get; init; } = FieldAlignment.Left;

    /// <summary>
    /// Gets or sets the format string to use when parsing date/time or numeric values.
    /// </summary>
    /// <remarks>
    /// For date/time types, this is passed to DateTime.ParseExact, DateTimeOffset.ParseExact, etc.
    /// For numeric types, this may affect parsing behavior.
    /// When omitted, default parsing rules apply.
    /// </remarks>
    public string? Format { get; init; }
}
