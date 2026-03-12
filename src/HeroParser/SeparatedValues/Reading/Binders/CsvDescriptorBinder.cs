using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.Validation;

namespace HeroParser.SeparatedValues.Reading.Binders;

/// <summary>
/// High-performance binder that uses pre-compiled property descriptors.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public sealed class CsvDescriptorBinder<T> : ICsvBinder<char, T> where T : new()
{
    private readonly CsvRecordDescriptor<T> descriptor;
    private readonly CultureInfo culture;
    private readonly bool caseSensitiveHeaders;
    private readonly bool allowMissingColumns;
    private readonly string[]? nullValues;

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
            ? [.. options.NullValues]
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
        var comparison = caseSensitiveHeaders ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        for (int i = 0; i < properties.Length; i++)
        {
            indices[i] = FindHeaderIndex(headerRow, properties[i].Name, comparison);

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
    private static int FindHeaderIndex(CsvRow<char> headerRow, string name, StringComparison comparison)
    {
        var target = name.AsSpan();
        var count = headerRow.ColumnCount;
        for (int i = 0; i < count; i++)
        {
            if (headerRow[i].Span.Equals(target, comparison))
                return i;
        }
        return -1;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryBind(CsvRow<char> row, int rowNumber, out T result, List<ValidationError>? errors = null)
    {
        result = descriptor.Factory();
        return BindInto(ref result, row, rowNumber, errors);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool BindInto(ref T instance, CsvRow<char> row, int rowNumber, List<ValidationError>? errors = null)
    {
        if (resolvedProperties is null)
            throw new InvalidOperationException("BindHeader must be called before BindInto when NeedsHeaderResolution is true.");

        var props = resolvedProperties;
        var columnCount = row.ColumnCount;
        var cultureLocal = culture;
        var nullVals = nullValues;
        bool hasErrors = false;

        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            var idx = prop.ColumnIndex;

            if ((uint)idx < (uint)columnCount)
            {
                var span = row[idx].Span;
                if (nullVals is null || !IsNullValue(span, nullVals))
                {
                    string? colName = !descriptor.UsesHeaderBinding ? null : prop.Name;

                    if (prop.IsRequired && span.IsEmpty)
                    {
                        hasErrors |= AddValidationError(
                            errors,
                            prop.Name,
                            rowNumber,
                            idx,
                            colName,
                            "Required",
                            "Value is required",
                            string.Empty);
                        continue;
                    }

                    try
                    {
                        prop.Setter(ref instance, span, cultureLocal);
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

                    if (prop.Validation is { HasAnyRule: true } validation)
                    {
                        hasErrors |= PropertyValidationRunner.Validate(
                            span, prop.Name, rowNumber, idx,
                            columnName: colName,
                            validation.NotEmpty, validation.MinLength, validation.MaxLength,
                            validation.RangeMin, validation.RangeMax, validation.Pattern,
                            errors,
                            cultureLocal);
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
        return !hasErrors;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AddValidationError(
        List<ValidationError>? errors,
        string propertyName,
        int rowNumber,
        int columnIndex,
        string? columnName,
        string rule,
        string message,
        string? rawValue)
    {
        errors?.Add(new ValidationError
        {
            PropertyName = propertyName,
            ColumnName = columnName,
            RawValue = rawValue,
            Rule = rule,
            Message = message,
            RowNumber = rowNumber,
            ColumnIndex = columnIndex
        });

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNullValue(ReadOnlySpan<char> value, string[] nullValues)
    {
        foreach (var nullValue in nullValues)
        {
            if (value.SequenceEqual(nullValue.AsSpan()))
                return true;
        }
        return false;
    }
}
