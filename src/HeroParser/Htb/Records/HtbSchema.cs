using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HeroParser;

namespace HeroParser.Htbs.Records;

/// <summary>
/// Represents the supported binary data types in the HTB format.
/// </summary>
public enum HtbDataType : byte
{
    /// <inheritdoc/>
    Unknown = 0,
    /// <inheritdoc/>
    Int32 = 1,
    /// <inheritdoc/>
    Int64 = 2,
    /// <inheritdoc/>
    Float = 3,
    /// <inheritdoc/>
    Double = 4,
    /// <inheritdoc/>
    Decimal = 5,
    /// <inheritdoc/>
    Boolean = 6,
    /// <inheritdoc/>
    DateTime = 7,
    /// <inheritdoc/>
    String = 8,
    /// <inheritdoc/>
    Guid = 9,
    /// <inheritdoc/>
    FloatArray = 10
}

/// <summary>
/// Describes a single column within an HTB schema.
/// </summary>
public sealed class HtbColumn
{
    /// <summary>Gets the name of the column.</summary>
    public string Name { get; }

    /// <summary>Gets the binary data type of the column.</summary>
    public HtbDataType DataType { get; }

    /// <summary>Gets a value indicating whether the column can contain null values.</summary>
    public bool IsNullable { get; }

    /// <summary>Gets the associated C# property metadata, if binding to a record.</summary>
    public PropertyInfo? Property { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="HtbColumn"/>.
    /// </summary>
    public HtbColumn(string name, HtbDataType dataType, bool isNullable, PropertyInfo? property = null)
    {
        Name = name;
        DataType = dataType;
        IsNullable = isNullable;
        Property = property;
    }
}

/// <summary>
/// Holds the schema definition of an HTB file or record structure.
/// </summary>
public sealed class HtbSchema
{
    /// <summary>Gets the list of columns in the schema in sequential order.</summary>
    public IReadOnlyList<HtbColumn> Columns { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="HtbSchema"/>.
    /// </summary>
    public HtbSchema(IReadOnlyList<HtbColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
        {
            throw new HtbException(HtbErrorCode.SerializationError, "Schema must contain at least one column.");
        }
        if (columns.Count > 2048)
        {
            throw new HtbException(HtbErrorCode.SerializationError, $"Schema column count {columns.Count} exceeds the maximum limit of 2048.");
        }
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i] == null)
            {
                throw new HtbException(HtbErrorCode.SerializationError, $"Column at index {i} cannot be null.");
            }
            if (string.IsNullOrWhiteSpace(columns[i].Name))
            {
                throw new HtbException(HtbErrorCode.SerializationError, $"Column name at index {i} cannot be null or empty.");
            }
        }
        Columns = columns;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<HtbSchema>> registeredSchemaProviders = new();

    /// <summary>
    /// Registers a compile-time schema provider for a type, making schema retrieval trim-safe.
    /// </summary>
    public static void RegisterSchemaProvider<T>(Func<HtbSchema> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        registeredSchemaProviders[typeof(T)] = provider;
    }

    /// <summary>
    /// Resolves the HTB schema for the specified record type using reflection.
    /// </summary>
    [RequiresUnreferencedCode("Schema extraction via reflection requires unreferenced code and is not safe for Native AOT.")]
    public static HtbSchema FromType<T>()
    {
        if (registeredSchemaProviders.TryGetValue(typeof(T), out var provider))
        {
            return provider();
        }

        var type = typeof(T);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var columns = new List<HtbColumn>();

        var sortedProps = properties
            .Select(p =>
            {
                var mapAttr = p.GetCustomAttribute<TabularMapAttribute>();
                int index = mapAttr?.Index ?? -1;
                string name = mapAttr?.Name ?? p.Name;
                return (Property: p, Name: name, Index: index);
            })
            .OrderBy(x => x.Index >= 0 ? x.Index : int.MaxValue)
            .ThenBy(x => x.Property.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var item in sortedProps)
        {
            var prop = item.Property;
            if (!prop.CanRead || !prop.CanWrite)
                continue;

            var propType = prop.PropertyType;
            bool isNullable = false;

            if (Nullable.GetUnderlyingType(propType) is { } underlying)
            {
                propType = underlying;
                isNullable = true;
            }
            else if (!propType.IsValueType || propType == typeof(string) || propType == typeof(float[]))
            {
                isNullable = true;
            }

            HtbDataType dataType = propType switch
            {
                _ when propType == typeof(int) => HtbDataType.Int32,
                _ when propType == typeof(long) => HtbDataType.Int64,
                _ when propType == typeof(float) => HtbDataType.Float,
                _ when propType == typeof(double) => HtbDataType.Double,
                _ when propType == typeof(decimal) => HtbDataType.Decimal,
                _ when propType == typeof(bool) => HtbDataType.Boolean,
                _ when propType == typeof(DateTime) => HtbDataType.DateTime,
                _ when propType == typeof(string) => HtbDataType.String,
                _ when propType == typeof(Guid) => HtbDataType.Guid,
                _ when propType == typeof(float[]) => HtbDataType.FloatArray,
                _ => HtbDataType.Unknown
            };

            if (dataType == HtbDataType.Unknown)
                continue; // Skip unsupported types

            columns.Add(new HtbColumn(item.Name, dataType, isNullable, prop));
        }

        return new HtbSchema(columns);
    }
}
