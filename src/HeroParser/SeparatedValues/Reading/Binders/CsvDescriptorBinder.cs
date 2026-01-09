using System.Globalization;
using System.Runtime.CompilerServices;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Binders;

/// <summary>
/// High-performance binder that uses pre-compiled property descriptors.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public sealed class CsvDescriptorBinder<T> : ICsvBinder<char, T> where T : class, new()
{
    private readonly CsvRecordDescriptor<T> descriptor;
    private readonly CultureInfo culture;
    private readonly bool caseSensitiveHeaders;
    private readonly bool allowMissingColumns;
    private readonly HashSet<string>? nullValues;

    // Resolved state after header processing
    private CsvPropertyDescriptor<T>[]? resolvedProperties;
    private bool resolved;

    /// <summary>
    /// Creates a new descriptor-based binder.
    /// </summary>
    public CsvDescriptorBinder(
        CsvRecordDescriptor<T> descriptor,
        CsvRecordOptions? options = null)
    {
        this.descriptor = descriptor;
        culture = options?.EffectiveCulture ?? CultureInfo.InvariantCulture;
        caseSensitiveHeaders = options?.CaseSensitiveHeaders ?? false;
        allowMissingColumns = options?.AllowMissingColumns ?? false;
        nullValues = options?.NullValues is { Count: > 0 }
            ? new HashSet<string>(options.NullValues, StringComparer.Ordinal)
            : null;

        // If not using header binding, resolve immediately
        if (!descriptor.UsesHeaderBinding)
        {
            resolvedProperties = descriptor.Properties;
            resolved = true;
        }
    }

    /// <inheritdoc />
    public bool NeedsHeaderResolution => !resolved;

    /// <inheritdoc />
    public void BindHeader(CsvRow<char> headerRow, int rowNumber)
    {
        if (resolved) return;

        var properties = descriptor.Properties;
        var indices = new int[properties.Length];
        var comparer = caseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        for (int i = 0; i < properties.Length; i++)
        {
            indices[i] = FindHeaderIndex(headerRow, properties[i].Name, comparer);

            if (indices[i] < 0 && properties[i].IsRequired && !allowMissingColumns)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Required column '{properties[i].Name}' not found in header row.",
                    rowNumber, 0);
            }
        }

        // Create resolved descriptor with actual column indices
        var resolvedDescriptor = descriptor.WithResolvedIndices(indices);
        resolvedProperties = resolvedDescriptor.Properties;
        resolved = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindHeaderIndex(CsvRow<char> headerRow, string name, StringComparer comparer)
    {
        var count = headerRow.ColumnCount;
        for (int i = 0; i < count; i++)
        {
            if (comparer.Equals(new string(headerRow[i].Span), name))
                return i;
        }
        return -1;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? Bind(CsvRow<char> row, int rowNumber)
    {
        var instance = descriptor.Factory();
        BindInto(instance, row, rowNumber);
        return instance;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool BindInto(T instance, CsvRow<char> row, int rowNumber)
    {
        if (resolvedProperties is null)
            throw new InvalidOperationException("BindHeader must be called before BindInto when NeedsHeaderResolution is true.");

        var props = resolvedProperties;
        var columnCount = row.ColumnCount;
        var cultureLocal = culture;
        var nullVals = nullValues;

        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            var idx = prop.ColumnIndex;

            if ((uint)idx < (uint)columnCount)
            {
                var span = row[idx].Span;
                if (nullVals is null || !IsNullValue(span, nullVals))
                {
                    try
                    {
                        prop.Setter(instance, span, cultureLocal);
                    }
                    catch (Exception ex) when (ex is not CsvException)
                    {
                        var fieldValue = new string(span);
                        throw new CsvException(
                            CsvErrorCode.ParseError,
                            $"Failed to parse '{fieldValue}' for property '{prop.Name}'.",
                            rowNumber,
                            idx + 1,
                            fieldValue,
                            ex);
                    }
                }
            }
            else if (idx >= 0 && prop.IsRequired && !allowMissingColumns)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row has only {columnCount} columns but '{prop.Name}' expects index {idx}.",
                    rowNumber, idx + 1);
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNullValue(ReadOnlySpan<char> value, HashSet<string> nullValues)
    {
        // Use span-based comparison to avoid string allocation per-field
        foreach (var nullValue in nullValues)
        {
            if (value.SequenceEqual(nullValue.AsSpan()))
                return true;
        }
        return false;
    }
}
