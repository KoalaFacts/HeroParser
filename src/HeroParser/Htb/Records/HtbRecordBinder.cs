using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HeroParser.Htbs.Records;

/// <summary>
/// A fast getter delegate for accessing property values.
/// </summary>
public delegate object? HtbGetter(object instance);

/// <summary>
/// A fast setter delegate for modifying property values.
/// </summary>
public delegate void HtbSetter(object instance, object? value);

/// <summary>
/// Provides fast, cached AOT-safe and expression-compiled property binders for HTB records.
/// </summary>
public sealed class HtbRecordBinder<T> : IHtbBinder<T> where T : new()
{
    private readonly HtbGetter[] getters;
    private readonly HtbSetter[] setters;

    /// <summary>
    /// Gets the schema associated with this binder.
    /// </summary>
    public HtbSchema Schema { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="HtbRecordBinder{T}"/> using reflection.
    /// </summary>
    [RequiresUnreferencedCode("Dynamic record binding requires reflection and is not safe for Native AOT.")]
    public HtbRecordBinder(HtbSchema schema)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        getters = new HtbGetter[schema.Columns.Count];
        setters = new HtbSetter[schema.Columns.Count];

        for (int i = 0; i < schema.Columns.Count; i++)
        {
            var col = schema.Columns[i];
            if (col.Property == null)
                continue;

            getters[i] = CreateGetter(col.Property);
            setters[i] = CreateSetter(col.Property);
        }
    }

    /// <summary>
    /// Checks if a column is bound.
    /// </summary>
    public bool IsColumnBound(int columnIndex)
    {
        return columnIndex >= 0 && columnIndex < setters.Length && setters[columnIndex] != null;
    }

    /// <summary>
    /// Binds a column value from the reader using dynamic reflection/expressions.
    /// </summary>
    public void BindField(T instance, int columnIndex, Reading.HtbRecordReader<T> reader, bool isNull)
    {
        if (isNull)
        {
            if (!Schema.Columns[columnIndex].IsNullable)
            {
                throw new HtbException(HtbErrorCode.CorruptData, $"Column '{Schema.Columns[columnIndex].Name}' is not nullable but null was read.");
            }
            SetValue(instance, columnIndex, null);
            return;
        }

        var dataType = Schema.Columns[columnIndex].DataType;
        object val = reader.ReadColumnValueInternal(dataType);
        SetValue(instance, columnIndex, val);
    }

    /// <summary>
    /// Binds a column value asynchronously from the reader using dynamic reflection/expressions.
    /// </summary>
    public async ValueTask BindFieldAsync(T instance, int columnIndex, Reading.HtbRecordReader<T> reader, bool isNull)
    {
        if (isNull)
        {
            if (!Schema.Columns[columnIndex].IsNullable)
            {
                throw new HtbException(HtbErrorCode.CorruptData, $"Column '{Schema.Columns[columnIndex].Name}' is not nullable but null was read.");
            }
            SetValue(instance, columnIndex, null);
            return;
        }

        var dataType = Schema.Columns[columnIndex].DataType;
        object val = await reader.ReadColumnValueInternalAsync(dataType);
        SetValue(instance, columnIndex, val);
    }

    /// <summary>
    /// Extracts values from the record instance into an array matching the schema order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetValue(T instance, int columnIndex)
    {
        return getters[columnIndex](instance!);
    }

    /// <summary>
    /// Binds a value to the corresponding column index of the record instance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValue(T instance, int columnIndex, object? value)
    {
        setters[columnIndex](instance!, value);
    }

    [RequiresUnreferencedCode("Expression compilation or reflection requires unreferenced code.")]
    private static HtbGetter CreateGetter(PropertyInfo property)
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            try
            {
                return CompileGetter(property);
            }
            catch
            {
                // Fallback to pure reflection
            }
        }

        return instance => property.GetValue(instance);
    }

    [RequiresUnreferencedCode("Expression compilation or reflection requires unreferenced code.")]
    private static HtbSetter CreateSetter(PropertyInfo property)
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            try
            {
                return CompileSetter(property);
            }
            catch
            {
                // Fallback to pure reflection
            }
        }

        return (instance, value) => property.SetValue(instance, value);
    }

    private static HtbGetter CompileGetter(PropertyInfo property)
    {
        var param = Expression.Parameter(typeof(object), "instance");
        var castInstance = Expression.Convert(param, typeof(T));
        var propertyAccess = Expression.Property(castInstance, property);
        var castResult = Expression.Convert(propertyAccess, typeof(object));
        var lambda = Expression.Lambda<Func<object, object?>>(castResult, param);
        var compiled = lambda.Compile();
        return instance => compiled(instance);
    }

    private static HtbSetter CompileSetter(PropertyInfo property)
    {
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var castInstance = Expression.Convert(instanceParam, typeof(T));
        var castValue = Expression.Convert(valueParam, property.PropertyType);
        var propertyAccess = Expression.Property(castInstance, property);
        var assign = Expression.Assign(propertyAccess, castValue);
        var lambda = Expression.Lambda<Action<object, object?>>(assign, instanceParam, valueParam);
        var compiled = lambda.Compile();
        return (instance, value) => compiled(instance, value);
    }
}
