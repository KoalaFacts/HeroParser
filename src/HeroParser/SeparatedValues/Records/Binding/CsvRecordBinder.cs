using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using HeroParser.SeparatedValues.Validation;
using System.Diagnostics.CodeAnalysis;
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

    [RequiresUnreferencedCode("Reflection-based binding may not work with trimming. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
    [RequiresDynamicCode("Reflection-based binding requires dynamic code. Use [CsvGenerateBinder] attribute for AOT support.")]
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

        // Check for duplicate headers if enabled
        if (recordOptions.DetectDuplicateHeaders)
        {
            DetectDuplicateHeaders(headerRow, rowNumber);
        }

        // Collect headers for validation
        var headers = new List<string>(headerRow.ColumnCount);
        for (int i = 0; i < headerRow.ColumnCount; i++)
        {
            headers.Add(headerRow[i].ToString());
        }

        // Validate required headers (upfront validation before processing)
        if (recordOptions.RequiredHeaders is { Count: > 0 })
        {
            ValidateRequiredHeaders(headers, rowNumber);
        }

        // Custom header validation callback
        if (recordOptions.ValidateHeaders is not null)
        {
            var context = new CsvHeaderValidationContext
            {
                Headers = headers,
                HeaderComparer = recordOptions.HeaderComparer
            };

            var result = recordOptions.ValidateHeaders(context);
            if (!result.IsValid)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    result.ErrorMessage ?? "Header validation failed.",
                    rowNumber);
            }
        }

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

    private void ValidateRequiredHeaders(List<string> headers, int rowNumber)
    {
        var headerSet = new HashSet<string>(headers, recordOptions.HeaderComparer);
        var missingHeaders = new List<string>();

        foreach (var requiredHeader in recordOptions.RequiredHeaders!)
        {
            if (!headerSet.Contains(requiredHeader))
            {
                missingHeaders.Add(requiredHeader);
            }
        }

        if (missingHeaders.Count > 0)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Required header(s) not found: {string.Join(", ", missingHeaders.Select(h => $"'{h}'"))}",
                rowNumber);
        }
    }

    private void DetectDuplicateHeaders(CsvCharSpanRow headerRow, int rowNumber)
    {
        var seen = new Dictionary<string, int>(recordOptions.HeaderComparer);

        for (int i = 0; i < headerRow.ColumnCount; i++)
        {
            var headerName = headerRow[i].ToString();
            if (seen.TryGetValue(headerName, out var firstIndex))
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Duplicate header '{headerName}' found at columns {firstIndex + 1} and {i + 1}.",
                    rowNumber);
            }
            seen[headerName] = i;
        }
    }

    /// <summary>
    /// Binds a CSV row to a record instance.
    /// </summary>
    /// <param name="row">The CSV row to bind.</param>
    /// <param name="rowNumber">The 1-based row number for error reporting.</param>
    /// <returns>The bound record, or <see langword="null"/> if the row should be skipped due to error handling.</returns>
    public T? Bind(CsvCharSpanRow row, int rowNumber)
    {
        EnsureResolved(rowNumber);

        var instance = new T();
        var validationEnabled = recordOptions.EnableValidation;
        var fieldValidators = validationEnabled ? recordOptions.FieldValidators : null;

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
            var rawValue = column.ToString();

            if (!binding.TryAssign(instance, column, out var boundValue))
            {
                // Check if there's an error handler
                if (recordOptions.OnDeserializeError is not null)
                {
                    var context = new CsvDeserializeErrorContext
                    {
                        Row = rowNumber,
                        Column = columnIndex + 1,
                        MemberName = binding.MemberName,
                        TargetType = binding.TargetType,
                        FieldValue = rawValue
                    };

                    var action = recordOptions.OnDeserializeError(context);
                    switch (action)
                    {
                        case DeserializeErrorAction.SkipRow:
                            return null;
                        case DeserializeErrorAction.UseDefault:
                            continue;
                        case DeserializeErrorAction.Throw:
                        default:
                            break;
                    }
                }

                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Failed to convert column {columnIndex + 1} to {binding.TargetType.Name} for member '{binding.MemberName}'.",
                    rowNumber,
                    columnIndex + 1,
                    rawValue);
            }

            // Run validators if validation is enabled
            if (fieldValidators is not null &&
                fieldValidators.TryGetValue(binding.MemberName, out var validators))
            {
                var skipRow = RunValidators(validators, boundValue, rawValue, binding.MemberName, rowNumber, columnIndex + 1);
                if (skipRow)
                {
                    return null;
                }
            }
        }

        return instance;
    }

    private bool RunValidators(
        IReadOnlyList<IFieldValidator> validators,
        object? value,
        string? rawValue,
        string fieldName,
        int rowNumber,
        int columnNumber)
    {
        foreach (var validator in validators)
        {
            var result = validator.Validate(value, rawValue);
            if (!result.IsValid)
            {
                var errorMessage = result.ErrorMessage ?? "Validation failed.";

                if (recordOptions.OnValidationError is not null)
                {
                    var context = new CsvValidationContext
                    {
                        Row = rowNumber,
                        Column = columnNumber,
                        FieldName = fieldName,
                        Value = value,
                        RawValue = rawValue
                    };

                    var action = recordOptions.OnValidationError(context, errorMessage);
                    switch (action)
                    {
                        case ValidationErrorAction.SkipRow:
                            return true; // Skip the row
                        case ValidationErrorAction.UseDefault:
                            return false; // Continue with default value (already set)
                        case ValidationErrorAction.Throw:
                        default:
                            break;
                    }
                }

                throw new CsvException(
                    CsvErrorCode.ValidationError,
                    $"Validation failed for field '{fieldName}': {errorMessage}",
                    rowNumber,
                    columnNumber,
                    rawValue);
            }
        }

        return false; // Don't skip the row
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
        var culture = recordOptions.EffectiveCulture;
        var customConverters = recordOptions.CustomConverters;

        foreach (var template in templates)
        {
            // Create converter with culture, format, and custom converters
            var converter = ConverterFactory.CreateConverter(
                template.TargetType,
                culture,
                template.Format,
                customConverters);

            // Wrap converter with null value checking if NullValues is configured
            if (recordOptions.NullValues is { Count: > 0 })
            {
                converter = WrapWithNullValueCheck(converter, recordOptions.NullValues);
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
            var format = attribute?.Format;
            var setter = SetterFactory.CreateSetter(property);

            bindings.Add(new BindingTemplate(
                property.Name,
                property.PropertyType,
                headerName,
                attributeIndex,
                format,
                setter));
        }

        return bindings;
    }
}
