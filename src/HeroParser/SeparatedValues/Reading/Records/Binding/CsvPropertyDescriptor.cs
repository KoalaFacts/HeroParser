using System.Globalization;
using System.Runtime.CompilerServices;

namespace HeroParser.SeparatedValues.Reading.Records.Binding;

/// <summary>
/// Delegate for setting a parsed value on a record instance.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
/// <param name="instance">The record instance to modify.</param>
/// <param name="value">The parsed character span value.</param>
/// <param name="culture">The culture for parsing.</param>
public delegate void CsvPropertySetter<T>(T instance, ReadOnlySpan<char> value, CultureInfo culture) where T : class;

/// <summary>
/// Describes a single property for CSV binding.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public readonly struct CsvPropertyDescriptor<T> where T : class
{
    /// <summary>Gets the property/column name.</summary>
    public string Name { get; }

    /// <summary>Gets the column index (for index-based binding) or -1 for header-based.</summary>
    public int ColumnIndex { get; }

    /// <summary>Gets the setter delegate for this property.</summary>
    public CsvPropertySetter<T> Setter { get; }

    /// <summary>Gets whether this property is required (non-nullable value type).</summary>
    public bool IsRequired { get; }

    /// <summary>
    /// Creates a new property descriptor.
    /// </summary>
    public CsvPropertyDescriptor(string name, int columnIndex, CsvPropertySetter<T> setter, bool isRequired = false)
    {
        Name = name;
        ColumnIndex = columnIndex;
        Setter = setter;
        IsRequired = isRequired;
    }

    /// <summary>
    /// Creates a new property descriptor for header-based binding.
    /// </summary>
    public CsvPropertyDescriptor(string name, CsvPropertySetter<T> setter, bool isRequired = false)
        : this(name, -1, setter, isRequired)
    {
    }
}

/// <summary>
/// Describes a record type for CSV binding with all its properties.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public sealed class CsvRecordDescriptor<T> where T : class, new()
{
    /// <summary>Gets the property descriptors.</summary>
    public CsvPropertyDescriptor<T>[] Properties { get; }

    /// <summary>Gets whether header-based binding is used.</summary>
    public bool UsesHeaderBinding { get; }

    /// <summary>Gets the factory function to create instances.</summary>
    public Func<T> Factory { get; }

    /// <summary>
    /// Creates a new record descriptor.
    /// </summary>
    /// <param name="properties">The property descriptors.</param>
    /// <param name="factory">Optional custom factory; defaults to parameterless constructor.</param>
    public CsvRecordDescriptor(CsvPropertyDescriptor<T>[] properties, Func<T>? factory = null)
    {
        Properties = properties;
        UsesHeaderBinding = properties.Length > 0 && properties[0].ColumnIndex < 0;
        Factory = factory ?? (() => new T());
    }

    /// <summary>
    /// Creates a resolved descriptor with column indices after header processing.
    /// </summary>
    internal CsvRecordDescriptor<T> WithResolvedIndices(int[] columnIndices)
    {
        var resolvedProperties = new CsvPropertyDescriptor<T>[Properties.Length];
        for (int i = 0; i < Properties.Length; i++)
        {
            var prop = Properties[i];
            resolvedProperties[i] = new CsvPropertyDescriptor<T>(
                prop.Name,
                columnIndices[i],
                prop.Setter,
                prop.IsRequired);
        }
        return new CsvRecordDescriptor<T>(resolvedProperties, Factory);
    }
}
