using System.Globalization;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Delegate for setting a parsed value on a record instance.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
/// <param name="instance">The record instance to modify.</param>
/// <param name="value">The parsed character span value.</param>
/// <param name="culture">The culture for parsing.</param>
public delegate void FixedWidthPropertySetter<T>(T instance, ReadOnlySpan<char> value, CultureInfo culture) where T : class;

/// <summary>
/// Describes a single property for fixed-width binding.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
/// <remarks>
/// Creates a new property descriptor.
/// </remarks>
public readonly struct FixedWidthPropertyDescriptor<T>(
    string name,
    int start,
    int length,
    char padChar,
    FieldAlignment alignment,
    FixedWidthPropertySetter<T> setter,
    bool isRequired = false) where T : class
{
    /// <summary>Gets the property/field name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the start position (0-based).</summary>
    public int Start { get; } = start;

    /// <summary>Gets the field length.</summary>
    public int Length { get; } = length;

    /// <summary>Gets the padding character.</summary>
    public char PadChar { get; } = padChar;

    /// <summary>Gets the field alignment.</summary>
    public FieldAlignment Alignment { get; } = alignment;

    /// <summary>Gets the setter delegate for this property.</summary>
    public FixedWidthPropertySetter<T> Setter { get; } = setter;

    /// <summary>Gets whether this property is required (non-nullable value type).</summary>
    public bool IsRequired { get; } = isRequired;
}

/// <summary>
/// Describes a record type for fixed-width binding with all its properties.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
/// <remarks>
/// Creates a new record descriptor.
/// </remarks>
/// <param name="properties">The property descriptors.</param>
/// <param name="factory">Optional custom factory; defaults to parameterless constructor.</param>
public sealed class FixedWidthRecordDescriptor<T>(FixedWidthPropertyDescriptor<T>[] properties, Func<T>? factory = null) where T : class, new()
{
    /// <summary>Gets the property descriptors.</summary>
    public FixedWidthPropertyDescriptor<T>[] Properties { get; } = properties;

    /// <summary>Gets the factory function to create instances.</summary>
    public Func<T> Factory { get; } = factory ?? (() => new T());
}
