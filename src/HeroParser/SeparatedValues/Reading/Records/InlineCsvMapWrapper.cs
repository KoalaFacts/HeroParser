using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using HeroParser.SeparatedValues.Mapping;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Records;

/// <summary>
/// Lightweight inline mapping wrapper that builds <see cref="CsvRecordDescriptor{T}"/> directly,
/// avoiding the <c>class</c> constraint on <see cref="CsvMap{T}"/>.
/// Property mutation correctness for value types is the caller's responsibility;
/// the <see cref="CsvRecordReaderBuilder{T}.Map{TProperty}"/> method documents this constraint.
/// </summary>
[RequiresUnreferencedCode("Inline mapping uses reflection and expression compilation.")]
[RequiresDynamicCode("Inline mapping uses expression compilation.")]
internal sealed class InlineCsvMapWrapper<T> : ICsvReadMapSource<T> where T : new()
{
    private readonly List<MappedEntry> entries = [];

    public void Map<TProperty>(Expression<Func<T, TProperty>> property, Action<CsvColumnBuilder>? configure)
    {
        var propertyInfo = ExtractPropertyInfo(property);

        if (entries.Exists(e => e.Name == propertyInfo.Name))
            throw new InvalidOperationException($"Property '{propertyInfo.Name}' has already been mapped.");

        var builder = new CsvColumnBuilder();
        configure?.Invoke(builder);

        var setter = CreateSetter<TProperty>(propertyInfo);
        entries.Add(new MappedEntry(propertyInfo.Name, builder, setter));
    }

    public CsvRecordDescriptor<T> BuildReadDescriptor()
    {
        var descriptors = new CsvPropertyDescriptor<T>[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var b = entry.Builder;
            var name = b.HeaderName ?? entry.Name;
            var columnIndex = b.ColumnIndex ?? -1;
            var validation = b.BuildValidation();

            descriptors[i] = new CsvPropertyDescriptor<T>(
                name,
                columnIndex,
                entry.Setter,
                b.IsRequired,
                validation);
        }

        return new CsvRecordDescriptor<T>(descriptors, static () => new T());
    }

    private static PropertyInfo ExtractPropertyInfo<TProperty>(Expression<Func<T, TProperty>> expression)
    {
        if (expression.Body is MemberExpression { Member: PropertyInfo propertyInfo })
            return propertyInfo;

        if (expression.Body is UnaryExpression { Operand: MemberExpression { Member: PropertyInfo innerProp } })
            return innerProp;

        throw new ArgumentException("Expression must select a property.", nameof(expression));
    }

    private static CsvPropertySetter<T> CreateSetter<TProperty>(PropertyInfo propertyInfo)
    {
        var instanceParam = Expression.Parameter(typeof(T), "instance");
        var valueParam = Expression.Parameter(typeof(TProperty), "value");
        var propertyAccess = Expression.Property(instanceParam, propertyInfo);
        var assign = Expression.Assign(propertyAccess, valueParam);
        var compiled = Expression.Lambda<Action<T, TProperty>>(assign, instanceParam, valueParam).Compile();

        var parse = SpanParserFactory.GetParser<TProperty>();

        void Setter(ref T instance, ReadOnlySpan<char> value, CultureInfo culture)
        {
            var parsed = parse(value, culture);
            compiled(instance, parsed);
        }

        return Setter;
    }

    private sealed class MappedEntry(string name, CsvColumnBuilder builder, CsvPropertySetter<T> setter)
    {
        public string Name { get; } = name;
        public CsvColumnBuilder Builder { get; } = builder;
        public CsvPropertySetter<T> Setter { get; } = setter;
    }
}
