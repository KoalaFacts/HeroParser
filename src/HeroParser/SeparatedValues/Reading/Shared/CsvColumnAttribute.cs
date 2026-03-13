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

    /// <summary>
    /// When <c>true</c>, the column must exist in the CSV header and the cell value must not be
    /// empty or whitespace-only. If validation fails, a <c>"NotNull"</c> validation error is
    /// collected and the entire row is excluded from results.
    /// </summary>
    /// <remarks>
    /// <para><b>On nullable types</b> (<c>string?</c>, <c>int?</c>, etc.): empty or whitespace
    /// cells normally resolve to <c>null</c>. Setting <c>NotNull = true</c> rejects those cells
    /// with a validation error instead.</para>
    /// <para><b>On non-nullable value types</b> (<c>decimal</c>, <c>int</c>, <c>bool</c>, etc.):
    /// the property keeps its <c>default</c> value (e.g., <c>0</c>, <c>false</c>) because parsing
    /// is skipped, and a validation error is collected.</para>
    /// <para><b>Without <c>NotNull</c></b> on a non-nullable value type: empty or whitespace cells
    /// throw a <see cref="Core.CsvException"/> because the value cannot
    /// be parsed into the target type.</para>
    /// </remarks>
    public bool NotNull { get; init; }

    /// <summary>
    /// When <c>true</c>, the string value must contain at least one non-whitespace character.
    /// If the cell is empty or contains only whitespace, a <c>"NotEmpty"</c> validation error is
    /// collected and the row is excluded from results. Only valid on <c>string</c> properties.
    /// </summary>
    /// <remarks>
    /// <para>This differs from <see cref="NotNull"/>: <c>NotNull</c> rejects empty cells,
    /// while <c>NotEmpty</c> additionally rejects whitespace-only cells like <c>"   "</c>.
    /// For string properties, you typically use both together.</para>
    /// </remarks>
    public bool NotEmpty { get; init; }

    /// <summary>
    /// Maximum allowed string length. A <c>"MaxLength"</c> validation error is collected when
    /// the parsed string exceeds this value. Set to <c>-1</c> (default) to disable.
    /// Only valid on <c>string</c> properties.
    /// </summary>
    public int MaxLength { get; init; } = -1;

    /// <summary>
    /// Minimum allowed string length. A <c>"MinLength"</c> validation error is collected when
    /// the parsed string is shorter than this value. Set to <c>-1</c> (default) to disable.
    /// Only valid on <c>string</c> properties.
    /// </summary>
    public int MinLength { get; init; } = -1;

    /// <summary>
    /// Minimum allowed numeric value (inclusive). A <c>"Range"</c> validation error is collected
    /// when the parsed number is below this value. Set to <see cref="double.NaN"/> (default)
    /// to disable. Only valid on numeric properties.
    /// </summary>
    public double RangeMin { get; init; } = double.NaN;

    /// <summary>
    /// Maximum allowed numeric value (inclusive). A <c>"Range"</c> validation error is collected
    /// when the parsed number exceeds this value. Set to <see cref="double.NaN"/> (default)
    /// to disable. Only valid on numeric properties.
    /// </summary>
    public double RangeMax { get; init; } = double.NaN;

    /// <summary>
    /// Regular expression pattern the string value must match. A <c>"Pattern"</c> validation
    /// error is collected when the value does not match. Only valid on <c>string</c> properties.
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Regex timeout in milliseconds for <see cref="Pattern"/> validation. Default is 1000ms.
    /// Prevents catastrophic backtracking on malicious input.
    /// </summary>
    public int PatternTimeoutMs { get; init; } = 1000;

    /// <summary>
    /// When <c>true</c>, this column is excluded from CSV output if <b>all</b> records have
    /// empty values (<see langword="null"/> or <c>""</c>) for it. Whitespace values are <b>not</b>
    /// considered empty. This is a <b>write-side</b> option.
    /// </summary>
    /// <remarks>
    /// <para>Works independently of <see cref="Writing.CsvWriteOptions.ExcludeEmptyColumns"/>:
    /// that option applies to all columns globally, while this targets specific columns.</para>
    /// <para>Requires materializing all records before writing. Not suitable for unbounded streaming.</para>
    /// </remarks>
    public bool ExcludeFromWriteIfAllEmpty { get; init; }
}
