using System.Diagnostics.CodeAnalysis;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Records.Binding;
using HeroParser.SeparatedValues.Reading.Span;

namespace HeroParser.SeparatedValues.Reading.Records.MultiSchema;

/// <summary>
/// Delegate for creating records from unmatched rows.
/// </summary>
/// <param name="discriminatorValue">The discriminator value that didn't match any registered type.</param>
/// <param name="columnValues">The column values from the row as strings.</param>
/// <param name="rowNumber">The 1-based row number.</param>
/// <returns>The created record, or <see langword="null"/> to skip the row.</returns>
public delegate object? CsvFallbackRowFactory(string discriminatorValue, string[] columnValues, int rowNumber);

/// <summary>
/// Dispatches CSV rows to the appropriate typed binder based on a discriminator column value.
/// </summary>
internal sealed class CsvMultiSchemaBinder
{
    private readonly string? discriminatorColumnName;
    private readonly Dictionary<string, Type> typeMappings;
    private readonly CsvFallbackRowFactory? fallbackFactory;
    private readonly UnmatchedRowBehavior unmatchedRowBehavior;
    private readonly CsvRecordOptions recordOptions;
    private readonly bool hasHeaderRow;
    private readonly StringComparison headerComparison;

    private int? resolvedDiscriminatorIndex;
    private bool headerProcessed;
    private readonly Dictionary<Type, IMultiSchemaBinder> typedBinders = [];

    public CsvMultiSchemaBinder(
        int? discriminatorColumnIndex,
        string? discriminatorColumnName,
        Dictionary<string, Type> typeMappings,
        CsvFallbackRowFactory? fallbackFactory,
        UnmatchedRowBehavior unmatchedRowBehavior,
        StringComparison discriminatorComparison,
        CsvRecordOptions recordOptions,
        bool hasHeaderRow,
        bool caseSensitiveHeaders)
    {
        this.discriminatorColumnName = discriminatorColumnName;
        this.typeMappings = new Dictionary<string, Type>(
            typeMappings,
            GetComparer(discriminatorComparison));
        this.fallbackFactory = fallbackFactory;
        this.unmatchedRowBehavior = unmatchedRowBehavior;
        this.recordOptions = recordOptions;
        this.hasHeaderRow = hasHeaderRow;
        headerComparison = caseSensitiveHeaders ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // If using index-based discriminator, it's already resolved
        if (discriminatorColumnIndex.HasValue)
        {
            resolvedDiscriminatorIndex = discriminatorColumnIndex.Value;
        }

        // If no header row, mark as processed
        if (!hasHeaderRow)
        {
            headerProcessed = true;
        }
    }

    /// <summary>
    /// Gets whether the header row needs to be processed to resolve column indices.
    /// </summary>
    public bool NeedsHeaderResolution => hasHeaderRow && !headerProcessed;

    /// <summary>
    /// Processes the header row to resolve column indices.
    /// </summary>
    public void BindHeader(CsvCharSpanRow headerRow, int rowNumber)
    {
        if (!hasHeaderRow || headerProcessed)
            return;

        // Resolve discriminator column index from header name
        if (discriminatorColumnName is not null)
        {
            var index = FindHeaderIndex(headerRow, discriminatorColumnName);
            if (index < 0)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Discriminator column '{discriminatorColumnName}' not found in header.",
                    rowNumber);
            }
            resolvedDiscriminatorIndex = index;
        }

        // Process header for all registered type binders
        foreach (var type in typeMappings.Values.Distinct())
        {
            var binder = GetOrCreateBinder(type);
            binder.BindHeader(headerRow, rowNumber);
        }

        headerProcessed = true;
    }

    /// <summary>
    /// Binds a CSV row to the appropriate record type based on the discriminator value.
    /// </summary>
    /// <param name="row">The CSV row to bind.</param>
    /// <param name="rowNumber">The 1-based row number for error reporting.</param>
    /// <returns>The bound record, or <see langword="null"/> if the row should be skipped.</returns>
    public object? Bind(CsvCharSpanRow row, int rowNumber)
    {
        var discriminatorValue = GetDiscriminatorValue(row, rowNumber);

        if (!typeMappings.TryGetValue(discriminatorValue, out var targetType))
        {
            return HandleUnmatchedRow(row, discriminatorValue, rowNumber);
        }

        return BindToType(targetType, row, rowNumber);
    }

    private string GetDiscriminatorValue(CsvCharSpanRow row, int rowNumber)
    {
        var index = resolvedDiscriminatorIndex
            ?? throw new InvalidOperationException("Discriminator column index not resolved.");

        if (index >= row.ColumnCount)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Discriminator column index {index} exceeds column count {row.ColumnCount}.",
                rowNumber,
                index + 1);
        }

        return row[index].ToString();
    }

    private object? HandleUnmatchedRow(CsvCharSpanRow row, string discriminatorValue, int rowNumber)
    {
        return unmatchedRowBehavior switch
        {
            UnmatchedRowBehavior.Skip => null,
            UnmatchedRowBehavior.UseFallback when fallbackFactory is not null
                => fallbackFactory(discriminatorValue, ExtractColumnValues(row), rowNumber),
            UnmatchedRowBehavior.UseFallback
                => throw new InvalidOperationException(
                    "UnmatchedRowBehavior.UseFallback requires a fallback factory to be configured."),
            _ => throw new CsvException(
                CsvErrorCode.ParseError,
                $"No type mapping found for discriminator value '{discriminatorValue}'.",
                rowNumber)
        };
    }

    private static string[] ExtractColumnValues(CsvCharSpanRow row)
    {
        var values = new string[row.ColumnCount];
        for (int i = 0; i < row.ColumnCount; i++)
        {
            values[i] = row[i].ToString();
        }
        return values;
    }

    private object? BindToType(Type type, CsvCharSpanRow row, int rowNumber)
    {
        var binder = GetOrCreateBinder(type);
        return binder.Bind(row, rowNumber);
    }

    private IMultiSchemaBinder GetOrCreateBinder(Type type)
    {
        if (typedBinders.TryGetValue(type, out var existingBinder))
        {
            return existingBinder;
        }

        var binder = CreateBinderForType(type);
        typedBinders[type] = binder;
        return binder;
    }

    [RequiresUnreferencedCode("Multi-schema binding requires reflection for dynamic type binding. Consider using [CsvGenerateBinder] attribute on record types.")]
    [RequiresDynamicCode("Multi-schema binding requires dynamic code generation.")]
    private IMultiSchemaBinder CreateBinderForType(Type type)
    {
        // Create a wrapper that holds the typed binder
        var wrapperType = typeof(MultiSchemaBinderWrapper<>).MakeGenericType(type);
        var wrapper = (IMultiSchemaBinder)Activator.CreateInstance(wrapperType, recordOptions)!;
        return wrapper;
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

    private static StringComparer GetComparer(StringComparison comparison)
    {
        return comparison switch
        {
            StringComparison.Ordinal => StringComparer.Ordinal,
            StringComparison.OrdinalIgnoreCase => StringComparer.OrdinalIgnoreCase,
            StringComparison.InvariantCulture => StringComparer.InvariantCulture,
            StringComparison.InvariantCultureIgnoreCase => StringComparer.InvariantCultureIgnoreCase,
            StringComparison.CurrentCulture => StringComparer.CurrentCulture,
            StringComparison.CurrentCultureIgnoreCase => StringComparer.CurrentCultureIgnoreCase,
            _ => StringComparer.Ordinal
        };
    }
}

/// <summary>
/// Internal interface for type-erased binder operations.
/// </summary>
internal interface IMultiSchemaBinder
{
    void BindHeader(CsvCharSpanRow headerRow, int rowNumber);
    object? Bind(CsvCharSpanRow row, int rowNumber);
}

/// <summary>
/// Wrapper that adapts the generic ICsvBinder{T} to the non-generic IMultiSchemaBinder interface.
/// </summary>
internal sealed class MultiSchemaBinderWrapper<T> : IMultiSchemaBinder where T : class, new()
{
    private readonly ICsvBinder<T> binder;

    public MultiSchemaBinderWrapper(CsvRecordOptions recordOptions)
    {
        if (!CsvRecordBinderFactory.TryCreateBinder(recordOptions, out ICsvBinder<T>? createdBinder) || createdBinder is null)
        {
            throw new InvalidOperationException($"No binder found for type {typeof(T).Name}. Ensure the type is decorated with [CsvGenerateBinder] attribute.");
        }
        binder = createdBinder;
    }

    public void BindHeader(CsvCharSpanRow headerRow, int rowNumber)
    {
        if (binder.NeedsHeaderResolution)
        {
            binder.BindHeader(headerRow, rowNumber);
        }
    }

    public object? Bind(CsvCharSpanRow row, int rowNumber)
    {
        return binder.Bind(row, rowNumber);
    }
}
