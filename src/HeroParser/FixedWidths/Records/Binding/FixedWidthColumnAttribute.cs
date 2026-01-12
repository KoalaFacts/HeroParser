using HeroParser.FixedWidths;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Declares how a fixed-width field maps to a property or field on a record type.
/// </summary>
/// <remarks>
/// You can specify the field bounds using either:
/// <list type="bullet">
/// <item><description><see cref="Start"/> and <see cref="Length"/> - specify starting position and field width</description></item>
/// <item><description><see cref="Start"/> and <see cref="End"/> - specify starting and ending positions (exclusive)</description></item>
/// </list>
/// If both <see cref="Length"/> and <see cref="End"/> are specified, <see cref="Length"/> takes precedence.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class FixedWidthColumnAttribute : Attribute
{
    private int lengthValue;
    private int endValue = -1;

    /// <summary>
    /// Gets or sets the zero-based starting position of the field in the record.
    /// </summary>
    public int Start { get; init; }

    /// <summary>
    /// Gets or sets the length of the field in characters.
    /// </summary>
    /// <remarks>
    /// If <see cref="End"/> is specified and <see cref="Length"/> is not explicitly set,
    /// the length is calculated as <c>End - Start</c>.
    /// </remarks>
    public int Length
    {
        get => lengthValue > 0 ? lengthValue : (endValue > Start ? endValue - Start : 0);
        init => lengthValue = value;
    }

    /// <summary>
    /// Gets or sets the zero-based ending position of the field (exclusive).
    /// </summary>
    /// <remarks>
    /// This is an alternative to specifying <see cref="Length"/>.
    /// The field spans from <see cref="Start"/> (inclusive) to <see cref="End"/> (exclusive).
    /// If both <see cref="Length"/> and <see cref="End"/> are specified, <see cref="Length"/> takes precedence.
    /// </remarks>
    /// <example>
    /// <code>
    /// // These two are equivalent:
    /// [FixedWidthColumn(Start = 0, Length = 10)]
    /// [FixedWidthColumn(Start = 0, End = 10)]
    /// </code>
    /// </example>
    public int End
    {
        get => endValue > 0 ? endValue : Start + lengthValue;
        init => endValue = value;
    }

    /// <summary>
    /// Gets or sets the padding character to trim from the field.
    /// When not specified, uses <see cref="FixedWidthReadOptions.DefaultPadChar"/>.
    /// </summary>
    public char PadChar { get; init; } = '\0'; // '\0' means "use default"

    /// <summary>
    /// Gets or sets the field alignment, which determines how trimming is applied.
    /// When not specified, uses <see cref="FixedWidthReadOptions.DefaultAlignment"/>.
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

