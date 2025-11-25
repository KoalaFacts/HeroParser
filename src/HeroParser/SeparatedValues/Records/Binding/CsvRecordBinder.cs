using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using System.Reflection;

namespace HeroParser;

internal sealed partial class CsvRecordBinder<T> where T : class, new()
{
    private readonly IReadOnlyList<MemberBinding> bindings;
    private readonly CsvRecordOptions recordOptions;
    private bool resolved;
    private readonly StringComparison headerComparison;

    private CsvRecordBinder(CsvRecordOptions recordOptions, IReadOnlyList<BindingTemplate> templates)
    {
        this.recordOptions = recordOptions;
        headerComparison = recordOptions.CaseSensitiveHeaders ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        bindings = InstantiateBindings(templates);

        if (!recordOptions.HasHeaderRow)
        {
            ResolveWithoutHeader();
        }
    }

    public static CsvRecordBinder<T> Create(CsvRecordOptions? options)
        => CreateFromTemplates(options, CreateTemplatesFromReflection());

    internal static CsvRecordBinder<T> CreateFromTemplates(
        CsvRecordOptions? options,
        IReadOnlyList<BindingTemplate> templates)
    {
        var resolvedOptions = options ?? CsvRecordOptions.Default;
        return new CsvRecordBinder<T>(resolvedOptions, templates);
    }

    public bool NeedsHeaderResolution => recordOptions.HasHeaderRow && !resolved;

    public void BindHeader(CsvCharSpanRow headerRow, int rowNumber)
    {
        if (!recordOptions.HasHeaderRow)
            return;

        foreach (var binding in bindings)
        {
            if (binding.AttributeIndex.HasValue)
            {
                binding.ResolvedIndex = binding.AttributeIndex.Value;
                continue;
            }

            var index = FindHeaderIndex(headerRow, binding.HeaderName);
            if (index >= 0)
            {
                binding.ResolvedIndex = index;
                continue;
            }

            if (!recordOptions.AllowMissingColumns)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Column '{binding.HeaderName}' not found in header.",
                    rowNumber);
            }
        }

        resolved = true;
    }

    public T Bind(CsvCharSpanRow row, int rowNumber)
    {
        EnsureResolved(rowNumber);

        var instance = new T();

        foreach (var binding in bindings)
        {
            if (!binding.ResolvedIndex.HasValue)
            {
                if (recordOptions.AllowMissingColumns)
                {
                    continue;
                }

                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"No column mapping found for member '{binding.MemberName}'.",
                    rowNumber);
            }

            var columnIndex = binding.ResolvedIndex.Value;
            if (columnIndex >= row.ColumnCount)
            {
                if (recordOptions.AllowMissingColumns)
                {
                    continue;
                }

                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row has only {row.ColumnCount} columns but '{binding.MemberName}' expects index {columnIndex}.",
                    rowNumber,
                    columnIndex + 1);
            }

            var column = row[columnIndex];
            if (!binding.TryAssign(instance, column))
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Failed to convert column {columnIndex + 1} to {binding.TargetType.Name} for member '{binding.MemberName}'.",
                    rowNumber,
                    columnIndex + 1);
            }
        }

        return instance;
    }

    private void EnsureResolved(int rowNumber)
    {
        if (resolved)
            return;

        if (recordOptions.HasHeaderRow)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                "Header row has not been processed yet.",
                rowNumber);
        }

        ResolveWithoutHeader();
    }

    private int FindHeaderIndex(CsvCharSpanRow headerRow, string headerName)
    {
        var target = headerName.AsSpan();
        for (int i = 0; i < headerRow.ColumnCount; i++)
        {
            var candidate = headerRow[i].CharSpan;
            if (headerComparison == StringComparison.Ordinal)
            {
                if (candidate.SequenceEqual(target))
                    return i;
            }
            else if (candidate.Equals(target, headerComparison))
            {
                return i;
            }
        }

        return -1;
    }

    private void ResolveWithoutHeader()
    {
        int ordinal = 0;
        foreach (var binding in bindings)
        {
            binding.ResolvedIndex = binding.AttributeIndex ?? ordinal++;
        }

        resolved = true;
    }

    private List<MemberBinding> InstantiateBindings(IReadOnlyList<BindingTemplate> templates)
    {
        var list = new List<MemberBinding>(templates.Count);
        foreach (var template in templates)
        {
            // Wrap converter with null value checking if NullValues is configured
            var converter = template.Converter;
            if (recordOptions.NullValues is { Count: > 0 })
            {
                converter = WrapWithNullValueCheck(template.Converter, recordOptions.NullValues);
            }

            list.Add(new MemberBinding(
                template.MemberName,
                template.TargetType,
                template.HeaderName,
                template.AttributeIndex,
                converter,
                template.Setter));
        }
        return list;
    }

    private static ColumnConverter WrapWithNullValueCheck(ColumnConverter original, IReadOnlyList<string> nullValues)
    {
        return (column, out value) =>
        {
            // Check if the column value matches any of the null values
            var columnStr = column.ToString();
            foreach (var nullValue in nullValues)
            {
                if (string.Equals(columnStr, nullValue, StringComparison.Ordinal))
                {
                    value = null;
                    return true;
                }
            }

            // If not a null value, proceed with normal conversion
            return original(column, out value);
        };
    }

    private static IReadOnlyList<BindingTemplate> CreateTemplatesFromReflection()
        => bindingCache.GetOrAdd(typeof(T), _ => BuildTemplates());

    private static List<BindingTemplate> BuildTemplates()
    {
        var members = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod is { IsStatic: false })
            .OrderBy(p => p.MetadataToken);

        var bindings = new List<BindingTemplate>();
        foreach (var property in members)
        {
            var attribute = property.GetCustomAttribute<CsvColumnAttribute>();
            var headerName = !string.IsNullOrWhiteSpace(attribute?.Name) ? attribute!.Name! : property.Name;
            int? attributeIndex = attribute is { Index: >= 0 } ? attribute.Index : null;
            var converter = ConverterFactory.CreateConverter(property.PropertyType);
            var setter = SetterFactory.CreateSetter(property);

            bindings.Add(new BindingTemplate(
                property.Name,
                property.PropertyType,
                headerName,
                attributeIndex,
                converter,
                setter));
        }

        return bindings;
    }
}
