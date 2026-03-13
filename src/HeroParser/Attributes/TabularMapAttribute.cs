namespace HeroParser;

/// <summary>
/// Declares how a column in a tabular format (CSV, Excel) maps to a property or field.
/// </summary>
/// <remarks>
/// On a <see cref="GenerateBinderAttribute"/>-decorated type, properties without
/// <see cref="TabularMapAttribute"/> default to mapping by property name.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class TabularMapAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the column header name. Defaults to the property/field name when omitted.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets or sets the zero-based column index. When set, takes precedence over <see cref="Name"/>.
    /// -1 means "use Name or property name".
    /// </summary>
    public int Index { get; init; } = -1;
}
