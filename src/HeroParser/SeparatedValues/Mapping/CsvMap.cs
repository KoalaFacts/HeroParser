using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.SeparatedValues.Mapping;

/// <summary>
/// Fluent mapping class that builds <see cref="CsvRecordDescriptor{T}"/> for reading
/// and <see cref="CsvRecordWriter{T}.WriterTemplate"/> arrays for writing.
/// Supports both inline configuration and subclass patterns.
/// </summary>
/// <typeparam name="T">The record type to map. Must be a reference type with a parameterless constructor.</typeparam>
/// <remarks>
/// <para>
/// The <c>class</c> constraint is required because <see cref="CsvPropertySetter{T}"/> takes
/// <c>ref T</c>, but compiled <see cref="Action{T, TProperty}"/> expression trees do not.
/// For reference types, the ref passes the object reference so property mutation works correctly.
/// For value types, mutations would be applied to a copy and silently lost.
/// </para>
/// <para>
/// Column order in write output follows <see cref="Map{TProperty}"/> call order,
/// not <see cref="CsvRecordWriter{T}.WriterTemplate.AttributeIndex"/>.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("CsvMap uses expression trees and reflection for property binding. Use [CsvGenerateBinder] for AOT/trimming support.")]
[RequiresDynamicCode("CsvMap uses expression trees and reflection. Use [CsvGenerateBinder] for AOT support.")]
public class CsvMap<T> : ICsvReadMapSource<T>, ICsvWriteMapSource<T> where T : class, new()
{
    private readonly List<MappedProperty> mappedProperties = [];

    /// <summary>
    /// Maps a property to a CSV column with optional configuration.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="property">An expression selecting the property to map (e.g., <c>t =&gt; t.Name</c>).</param>
    /// <param name="configure">Optional configuration action for the column builder.</param>
    /// <returns>This instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">The expression does not select a property.</exception>
    /// <exception cref="InvalidOperationException">The property has already been mapped.</exception>
    public CsvMap<T> Map<TProperty>(Expression<Func<T, TProperty>> property, Action<CsvColumnBuilder>? configure = null)
    {
        var propertyInfo = ExtractPropertyInfo(property);

        if (mappedProperties.Exists(m => m.PropertyInfo.Name == propertyInfo.Name))
            throw new InvalidOperationException($"Property '{propertyInfo.Name}' has already been mapped.");

        var builder = new CsvColumnBuilder();
        configure?.Invoke(builder);

        mappedProperties.Add(new MappedProperty(propertyInfo, typeof(TProperty), builder, CreateSetterForProperty<TProperty>(propertyInfo)));
        return this;
    }

    /// <summary>
    /// Builds a <see cref="CsvRecordDescriptor{T}"/> for reading CSV data using this mapping.
    /// </summary>
    /// <returns>A record descriptor configured with property setters, column bindings, and validation rules.</returns>
    public CsvRecordDescriptor<T> BuildReadDescriptor()
    {
        var descriptors = new CsvPropertyDescriptor<T>[mappedProperties.Count];

        for (int i = 0; i < mappedProperties.Count; i++)
        {
            var mapped = mappedProperties[i];
            var b = mapped.Builder;
            var name = b.HeaderName ?? mapped.PropertyInfo.Name;
            var columnIndex = b.ColumnIndex ?? -1;
            var validation = b.BuildValidation();

            descriptors[i] = new CsvPropertyDescriptor<T>(
                name,
                columnIndex,
                mapped.Setter,
                b.IsNotNull,
                validation);
        }

        return new CsvRecordDescriptor<T>(descriptors, static () => new T());
    }

    /// <summary>
    /// Builds an array of <see cref="CsvRecordWriter{T}.WriterTemplate"/> for writing CSV data using this mapping.
    /// Column order follows <see cref="Map{TProperty}"/> call order.
    /// </summary>
    /// <returns>Writer templates configured with header names, formats, and property getters.</returns>
    public CsvRecordWriter<T>.WriterTemplate[] BuildWriteTemplates()
    {
        var templates = new CsvRecordWriter<T>.WriterTemplate[mappedProperties.Count];

        for (int i = 0; i < mappedProperties.Count; i++)
        {
            var mapped = mappedProperties[i];
            var b = mapped.Builder;
            var headerName = b.HeaderName ?? mapped.PropertyInfo.Name;
            var getter = CreateGetter(mapped.PropertyInfo);

            templates[i] = new CsvRecordWriter<T>.WriterTemplate(
                MemberName: mapped.PropertyInfo.Name,
                SourceType: mapped.PropertyType,
                HeaderName: headerName,
                AttributeIndex: b.ColumnIndex,
                Format: b.FormatString,
                Getter: getter);
        }

        return templates;
    }

    private static PropertyInfo ExtractPropertyInfo<TProperty>(Expression<Func<T, TProperty>> expression)
    {
        if (expression.Body is MemberExpression { Member: PropertyInfo propertyInfo })
            return propertyInfo;

        // Handle cases where the compiler inserts a Convert node (e.g., for value types)
        if (expression.Body is UnaryExpression { Operand: MemberExpression { Member: PropertyInfo innerProp } })
            return innerProp;

        throw new ArgumentException("Expression must select a property.", nameof(expression));
    }

    private static CsvPropertySetter<T> CreateSetterForProperty<TProperty>(PropertyInfo propertyInfo)
    {
        // Compile Action<T, TProperty> for the property setter
        var instanceParam = Expression.Parameter(typeof(T), "instance");
        var valueParam = Expression.Parameter(typeof(TProperty), "value");
        var propertyAccess = Expression.Property(instanceParam, propertyInfo);
        var assign = Expression.Assign(propertyAccess, valueParam);
        var compiled = Expression.Lambda<Action<T, TProperty>>(assign, instanceParam, valueParam).Compile();

        // Get the parser for this property type
        var parse = SpanParserFactory.GetParser<TProperty>();

        void Setter(ref T instance, ReadOnlySpan<char> value, CultureInfo culture)
        {
            var parsed = parse(value, culture);
            compiled(instance, parsed);
        }

        return Setter;
    }

    private static Func<T, object?> CreateGetter(PropertyInfo propertyInfo)
    {
        var instanceParam = Expression.Parameter(typeof(T), "instance");
        var propertyAccess = Expression.Property(instanceParam, propertyInfo);

        Expression body = propertyInfo.PropertyType.IsValueType
            ? Expression.Convert(propertyAccess, typeof(object))
            : Expression.TypeAs(propertyAccess, typeof(object));

        return Expression.Lambda<Func<T, object?>>(body, instanceParam).Compile();
    }

    private sealed class MappedProperty(PropertyInfo propertyInfo, Type propertyType, CsvColumnBuilder builder, CsvPropertySetter<T> setter)
    {
        public PropertyInfo PropertyInfo { get; } = propertyInfo;
        public Type PropertyType { get; } = propertyType;
        public CsvColumnBuilder Builder { get; } = builder;
        public CsvPropertySetter<T> Setter { get; } = setter;
    }
}
