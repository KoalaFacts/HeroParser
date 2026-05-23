using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using HeroParser.FixedWidths.Records.Binding;
using HeroParser.SeparatedValues.Mapping;

namespace HeroParser.FixedWidths.Mapping;

/// <summary>
/// Lightweight inline mapping wrapper that builds <see cref="FixedWidthRecordDescriptor{T}"/> directly,
/// avoiding the <c>class</c> constraint on <see cref="FixedWidthMap{T}"/>.
/// </summary>
[RequiresUnreferencedCode("Inline mapping uses reflection and expression compilation.")]
[RequiresDynamicCode("Inline mapping uses expression compilation.")]
internal sealed class InlineFixedWidthMapWrapper<T> : IFixedWidthReadMapSource<T> where T : new()
{
    private readonly List<MappedEntry> entries = [];

    /// <summary>
    /// Gets whether any entry has a configured header name for validation.
    /// </summary>
    internal bool HasHeaderNames => entries.Exists(e => e.Builder.HeaderName is not null);

    public void Map<TProperty>(Expression<Func<T, TProperty>> property, Action<FixedWidthFieldBuilder>? configure)
    {
        var propertyInfo = ExtractPropertyInfo(property);

        if (entries.Exists(e => e.Name == propertyInfo.Name))
            throw new InvalidOperationException($"Property '{propertyInfo.Name}' has already been mapped.");

        var builder = new FixedWidthFieldBuilder();
        configure?.Invoke(builder);

        var setter = CreateSetter<TProperty>(propertyInfo);
        var byteSetter = CreateByteSetter<TProperty>(propertyInfo);
        entries.Add(new MappedEntry(propertyInfo.Name, builder, setter, byteSetter));
    }

    public FixedWidthRecordDescriptor<T> BuildReadDescriptor()
        => BuildReadDescriptor(headerRow: null, caseSensitive: false);

    /// <summary>
    /// Builds a read descriptor, optionally validating field header names against the provided header row text.
    /// </summary>
    /// <param name="headerRow">The raw header row text, or null to skip header validation.</param>
    /// <param name="caseSensitive">Whether header name comparison is case-sensitive.</param>
    internal FixedWidthRecordDescriptor<T> BuildReadDescriptor(string? headerRow, bool caseSensitive)
    {
        var descriptors = new FixedWidthPropertyDescriptor<T>[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var b = entry.Builder;

            var start = b.StartPosition ?? throw new InvalidOperationException(
                $"Property '{entry.Name}' must have a Start position configured.");
            var length = b.ResolvedFieldLength ?? throw new InvalidOperationException(
                $"Property '{entry.Name}' must have a Length or End configured.");
            var padChar = b.FieldPadChar ?? ' ';
            var alignment = b.FieldAlignment ?? FieldAlignment.Left;

            // Validate header name against the actual header row when requested
            if (headerRow is not null && b.HeaderName is not null)
            {
                var actualHeader = ExtractHeaderValue(headerRow, start, length, padChar, alignment);
                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                if (!actualHeader.Equals(b.HeaderName, comparison))
                {
                    throw new InvalidOperationException(
                        $"Header mismatch for property '{entry.Name}': expected '{b.HeaderName}' but found '{actualHeader}'.");
                }
            }

            descriptors[i] = new FixedWidthPropertyDescriptor<T>(
                entry.Name,
                start,
                length,
                padChar,
                alignment,
                entry.Setter,
                isNotNull: false,
                validation: null,
                entry.ByteSetter);
        }

        return new FixedWidthRecordDescriptor<T>(descriptors, static () => new T());
    }

    private static string ExtractHeaderValue(string headerRow, int start, int length, char padChar, FieldAlignment alignment)
    {
        // If the header row is shorter than the field start, the field is absent — return empty
        if (start >= headerRow.Length)
            return string.Empty;

        var end = Math.Min(start + length, headerRow.Length);
        var raw = headerRow.AsSpan(start, end - start);

        // Trim padding based on alignment: left-aligned content is right-padded, right-aligned is left-padded
        return alignment == FieldAlignment.Right
            ? raw.TrimStart(padChar).ToString()
            : raw.TrimEnd(padChar).ToString();
    }

    private static PropertyInfo ExtractPropertyInfo<TProperty>(Expression<Func<T, TProperty>> expression)
    {
        if (expression.Body is MemberExpression { Member: PropertyInfo propertyInfo })
            return propertyInfo;

        if (expression.Body is UnaryExpression { Operand: MemberExpression { Member: PropertyInfo innerProp } })
            return innerProp;

        throw new ArgumentException("Expression must select a property.", nameof(expression));
    }

    private static FixedWidthPropertySetter<T> CreateSetter<TProperty>(PropertyInfo propertyInfo)
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

    private static FixedWidthBytePropertySetter<T> CreateByteSetter<TProperty>(PropertyInfo propertyInfo)
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

    private sealed class MappedEntry(
        string name,
        FixedWidthFieldBuilder builder,
        FixedWidthPropertySetter<T> setter,
        FixedWidthBytePropertySetter<T> byteSetter)
    {
        public string Name { get; } = name;
        public FixedWidthFieldBuilder Builder { get; } = builder;
        public FixedWidthPropertySetter<T> Setter { get; } = setter;
        public FixedWidthBytePropertySetter<T> ByteSetter { get; } = byteSetter;
    }
}
