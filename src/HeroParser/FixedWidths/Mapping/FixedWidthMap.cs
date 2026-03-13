using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using HeroParser.FixedWidths.Records.Binding;
using HeroParser.FixedWidths.Writing;
using HeroParser.SeparatedValues.Mapping;

namespace HeroParser.FixedWidths.Mapping;

/// <summary>
/// Fluent mapping class that builds <see cref="FixedWidthRecordDescriptor{T}"/> for reading
/// and <see cref="FixedWidthRecordWriter{T}.WriterTemplate"/> arrays for writing.
/// Supports both inline configuration and subclass patterns.
/// </summary>
/// <typeparam name="T">The record type to map. Must be a reference type with a parameterless constructor.</typeparam>
/// <remarks>
/// The <c>class</c> constraint is required because <see cref="FixedWidthPropertySetter{T}"/> takes
/// <c>ref T</c>, but compiled <see cref="Action{T, TProperty}"/> expression trees do not.
/// For reference types, the ref passes the object reference so property mutation works correctly.
/// </remarks>
[RequiresUnreferencedCode("FixedWidthMap uses expression trees and reflection for property binding. Use [FixedWidthGenerateBinder] for AOT/trimming support.")]
[RequiresDynamicCode("FixedWidthMap uses expression trees and reflection. Use [FixedWidthGenerateBinder] for AOT support.")]
public class FixedWidthMap<T> : IFixedWidthReadMapSource<T>, IFixedWidthWriteMapSource<T> where T : class, new()
{
    private readonly List<MappedProperty> mappedProperties = [];

    /// <summary>
    /// Maps a property to a fixed-width field with configuration.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="property">An expression selecting the property to map (e.g., <c>t =&gt; t.Name</c>).</param>
    /// <param name="configure">Configuration action for the column builder. Must set Start and Length (or End).</param>
    /// <returns>This instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">The configure action is null.</exception>
    /// <exception cref="ArgumentException">The expression does not select a property.</exception>
    /// <exception cref="InvalidOperationException">The property has already been mapped.</exception>
    public FixedWidthMap<T> Map<TProperty>(Expression<Func<T, TProperty>> property, Action<FixedWidthColumnBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var propertyInfo = ExtractPropertyInfo(property);

        if (mappedProperties.Exists(m => m.PropertyInfo.Name == propertyInfo.Name))
            throw new InvalidOperationException($"Property '{propertyInfo.Name}' has already been mapped.");

        var builder = new FixedWidthColumnBuilder();
        configure(builder);

        mappedProperties.Add(new MappedProperty(
            propertyInfo,
            typeof(TProperty),
            builder,
            CreateSetterForProperty<TProperty>(propertyInfo),
            CreateByteSetterForProperty<TProperty>(propertyInfo)));
        return this;
    }

    /// <summary>
    /// Builds a <see cref="FixedWidthRecordDescriptor{T}"/> for reading fixed-width data using this mapping.
    /// </summary>
    /// <returns>A record descriptor configured with property setters, column positions, and validation rules.</returns>
    /// <exception cref="InvalidOperationException">A mapped property is missing a Start position or Length/End.</exception>
    public FixedWidthRecordDescriptor<T> BuildReadDescriptor()
    {
        var descriptors = new FixedWidthPropertyDescriptor<T>[mappedProperties.Count];

        for (int i = 0; i < mappedProperties.Count; i++)
        {
            var mapped = mappedProperties[i];
            var b = mapped.Builder;

            var start = b.StartPosition ?? throw new InvalidOperationException(
                $"Property '{mapped.PropertyInfo.Name}' must have a Start position configured.");
            var length = b.ResolvedFieldLength ?? throw new InvalidOperationException(
                $"Property '{mapped.PropertyInfo.Name}' must have a Length or End configured.");
            var padChar = b.FieldPadChar ?? ' ';
            var alignment = b.FieldAlignment ?? FieldAlignment.Left;
            var validation = b.BuildValidation();

            descriptors[i] = new FixedWidthPropertyDescriptor<T>(
                mapped.PropertyInfo.Name,
                start,
                length,
                padChar,
                alignment,
                mapped.Setter,
                b.IsNotNull,
                validation,
                mapped.ByteSetter);
        }

        return new FixedWidthRecordDescriptor<T>(descriptors, static () => new T());
    }

    /// <summary>
    /// Builds an array of <see cref="FixedWidthRecordWriter{T}.WriterTemplate"/> for writing fixed-width data.
    /// Column order follows <see cref="Map{TProperty}"/> call order.
    /// </summary>
    /// <returns>Writer templates configured with positions, alignments, formats, and property getters.</returns>
    /// <exception cref="InvalidOperationException">A mapped property is missing a Start position or Length/End.</exception>
    public FixedWidthRecordWriter<T>.WriterTemplate[] BuildWriteTemplates()
    {
        var templates = new FixedWidthRecordWriter<T>.WriterTemplate[mappedProperties.Count];

        for (int i = 0; i < mappedProperties.Count; i++)
        {
            var mapped = mappedProperties[i];
            var b = mapped.Builder;

            var start = b.StartPosition ?? throw new InvalidOperationException(
                $"Property '{mapped.PropertyInfo.Name}' must have a Start position configured.");
            var length = b.ResolvedFieldLength ?? throw new InvalidOperationException(
                $"Property '{mapped.PropertyInfo.Name}' must have a Length or End configured.");
            var alignment = b.FieldAlignment ?? FieldAlignment.Left;
            var padChar = b.FieldPadChar ?? ' ';
            var getter = CreateGetter(mapped.PropertyInfo);

            templates[i] = new FixedWidthRecordWriter<T>.WriterTemplate(
                MemberName: mapped.PropertyInfo.Name,
                SourceType: mapped.PropertyType,
                Start: start,
                Length: length,
                Alignment: alignment,
                PadChar: padChar,
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

    private static FixedWidthPropertySetter<T> CreateSetterForProperty<TProperty>(PropertyInfo propertyInfo)
    {
        // Compile Action<T, TProperty> for the property setter
        var instanceParam = Expression.Parameter(typeof(T), "instance");
        var valueParam = Expression.Parameter(typeof(TProperty), "value");
        var propertyAccess = Expression.Property(instanceParam, propertyInfo);
        var assign = Expression.Assign(propertyAccess, valueParam);
        var compiled = Expression.Lambda<Action<T, TProperty>>(assign, instanceParam, valueParam).Compile();

        // Get the parser for this property type (reuse SpanParserFactory from CSV side)
        var parse = SpanParserFactory.GetParser<TProperty>();

        void Setter(ref T instance, ReadOnlySpan<char> value, CultureInfo culture)
        {
            var parsed = parse(value, culture);
            compiled(instance, parsed);
        }

        return Setter;
    }

    private static FixedWidthBytePropertySetter<T> CreateByteSetterForProperty<TProperty>(PropertyInfo propertyInfo)
    {
        var instanceParam = Expression.Parameter(typeof(T), "instance");
        var valueParam = Expression.Parameter(typeof(TProperty), "value");
        var propertyAccess = Expression.Property(instanceParam, propertyInfo);
        var assign = Expression.Assign(propertyAccess, valueParam);
        var compiled = Expression.Lambda<Action<T, TProperty>>(assign, instanceParam, valueParam).Compile();

        var parse = Utf8SpanParserFactory.GetParser<TProperty>();

        void Setter(ref T instance, ReadOnlySpan<byte> value, CultureInfo culture)
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

    private sealed class MappedProperty(
        PropertyInfo propertyInfo,
        Type propertyType,
        FixedWidthColumnBuilder builder,
        FixedWidthPropertySetter<T> setter,
        FixedWidthBytePropertySetter<T> byteSetter)
    {
        public PropertyInfo PropertyInfo { get; } = propertyInfo;
        public Type PropertyType { get; } = propertyType;
        public FixedWidthColumnBuilder Builder { get; } = builder;
        public FixedWidthPropertySetter<T> Setter { get; } = setter;
        public FixedWidthBytePropertySetter<T> ByteSetter { get; } = byteSetter;
    }
}
