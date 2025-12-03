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
public readonly struct FixedWidthPropertyDescriptor<T> where T : class
{
    /// <summary>Gets the property/field name.</summary>
    public string Name { get; }

    /// <summary>Gets the start position (0-based).</summary>
    public int Start { get; }

    /// <summary>Gets the field length.</summary>
    public int Length { get; }

    /// <summary>Gets the padding character.</summary>
    public char PadChar { get; }

    /// <summary>Gets the field alignment.</summary>
    public FieldAlignment Alignment { get; }

    /// <summary>Gets the setter delegate for this property.</summary>
    public FixedWidthPropertySetter<T> Setter { get; }

    /// <summary>Gets whether this property is required (non-nullable value type).</summary>
    public bool IsRequired { get; }

    /// <summary>
    /// Creates a new property descriptor.
    /// </summary>
    public FixedWidthPropertyDescriptor(
        string name,
        int start,
        int length,
        char padChar,
        FieldAlignment alignment,
        FixedWidthPropertySetter<T> setter,
        bool isRequired = false)
    {
        Name = name;
        Start = start;
        Length = length;
        PadChar = padChar;
        Alignment = alignment;
        Setter = setter;
        IsRequired = isRequired;
    }
}

/// <summary>
/// Describes a record type for fixed-width binding with all its properties.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public sealed class FixedWidthRecordDescriptor<T> where T : class, new()
{
    /// <summary>Gets the property descriptors.</summary>
    public FixedWidthPropertyDescriptor<T>[] Properties { get; }

    /// <summary>Gets the factory function to create instances.</summary>
    public Func<T> Factory { get; }

    /// <summary>
    /// Creates a new record descriptor.
    /// </summary>
    /// <param name="properties">The property descriptors.</param>
    /// <param name="factory">Optional custom factory; defaults to parameterless constructor.</param>
    public FixedWidthRecordDescriptor(FixedWidthPropertyDescriptor<T>[] properties, Func<T>? factory = null)
    {
        Properties = properties;
        Factory = factory ?? (() => new T());
    }
}
