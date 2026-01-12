using HeroParser.FixedWidths;

namespace HeroParser.FixedWidths.Reading.Data;

/// <summary>
/// Defines a fixed-width column for <see cref="FixedWidthDataReader"/>.
/// </summary>
public sealed record FixedWidthDataReaderColumn
{
    /// <summary>
    /// Gets or sets the zero-based starting position of the column.
    /// </summary>
    public int Start { get; init; }

    /// <summary>
    /// Gets or sets the length of the column in characters.
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// Gets or sets the column name. When <see langword="null"/>, header values or defaults are used.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets or sets the padding character to trim. When <see langword="null"/>, uses parser defaults.
    /// </summary>
    public char? PadChar { get; init; }

    /// <summary>
    /// Gets or sets the field alignment. When <see langword="null"/>, uses parser defaults.
    /// </summary>
    public FieldAlignment? Alignment { get; init; }
}
