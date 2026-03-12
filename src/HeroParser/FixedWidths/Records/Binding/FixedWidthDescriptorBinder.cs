using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.FixedWidths.Mapping;
using HeroParser.Validation;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// High-performance binder that uses pre-compiled property descriptors.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public sealed class FixedWidthDescriptorBinder<T> : IFixedWidthBinder<T>, IFixedWidthByteBinder<T> where T : new()
{
    private readonly FixedWidthRecordDescriptor<T> descriptor;
    private readonly CultureInfo culture;
    private readonly string[]? nullValues;
    private readonly byte[][]? nullValuesUtf8;

    /// <summary>
    /// Creates a new descriptor-based binder.
    /// </summary>
    public FixedWidthDescriptorBinder(
        FixedWidthRecordDescriptor<T> descriptor,
        CultureInfo? culture = null,
        IReadOnlyList<string>? nullValues = null)
    {
        this.descriptor = descriptor;
        this.culture = culture ?? CultureInfo.InvariantCulture;
        this.nullValues = nullValues is { Count: > 0 }
            ? [.. nullValues]
            : null;
        nullValuesUtf8 = nullValues is { Count: > 0 }
            ? EncodeNullValues(nullValues)
            : null;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryBind(FixedWidthCharSpanRow row, out T result, List<ValidationError>? errors = null)
    {
        var instance = descriptor.Factory();
        if (!BindInto(ref instance, row, errors))
        {
            result = default!;
            return false;
        }

        result = instance;
        return true;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool BindInto(ref T instance, FixedWidthCharSpanRow row, List<ValidationError>? errors = null)
    {
        var props = descriptor.Properties;
        var cultureLocal = culture;
        var nullVals = nullValues;
        bool hasErrors = false;

        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            var field = row.GetField(prop.Start, prop.Length, prop.PadChar, prop.Alignment);
            var span = field.CharSpan;

            if (nullVals is null || !IsNullValue(span, nullVals))
            {
                if (prop.IsRequired && (span.IsEmpty || span.Trim().IsEmpty))
                {
                    hasErrors |= AddValidationError(
                        errors,
                        prop.Name,
                        row.RecordNumber,
                        prop.Start,
                        columnName: null,
                        "Required",
                        "Value is required",
                        new string(span));
                    continue;
                }

                try
                {
                    prop.Setter(ref instance, span, cultureLocal);
                }
                catch (Exception ex) when (ex is not FixedWidthException)
                {
                    var fieldValue = new string(span);
                    throw new FixedWidthException(
                        FixedWidthErrorCode.ParseError,
                        $"Failed to parse '{fieldValue}' for property '{prop.Name}': {ex.Message}",
                        row.RecordNumber,
                        row.SourceLineNumber);
                }

                if (prop.Validation is { HasAnyRule: true } validation)
                {
                    hasErrors |= PropertyValidationRunner.Validate(
                        span, prop.Name, row.RecordNumber, prop.Start,
                        columnName: null,
                        validation.NotEmpty, validation.MinLength, validation.MaxLength,
                        validation.RangeMin, validation.RangeMax, validation.Pattern,
                        errors,
                        cultureLocal);
                }
            }
        }
        return !hasErrors;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryBind(FixedWidthByteSpanRow row, out T result, List<ValidationError>? errors = null)
    {
        var instance = descriptor.Factory();
        if (!BindInto(ref instance, row, errors))
        {
            result = default!;
            return false;
        }

        result = instance;
        return true;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool BindInto(ref T instance, FixedWidthByteSpanRow row, List<ValidationError>? errors = null)
    {
        var props = descriptor.Properties;
        var cultureLocal = culture;
        var nullValsUtf8 = nullValuesUtf8;
        bool hasErrors = false;

        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            var field = row.GetField(prop.Start, prop.Length, (byte)prop.PadChar, prop.Alignment);
            var span = field.ByteSpan;

            if (nullValsUtf8 is null || !IsNullValue(span, nullValsUtf8))
            {
                if (prop.IsRequired && FixedWidthUtf8BindingHelper.IsNullOrWhiteSpace(span))
                {
                    hasErrors |= AddValidationError(
                        errors,
                        prop.Name,
                        row.RecordNumber,
                        prop.Start,
                        columnName: null,
                        "Required",
                        "Value is required",
                        errors is null ? null : FixedWidthUtf8BindingHelper.Decode(span));
                    continue;
                }

                try
                {
                    if (prop.ByteSetter is not null)
                    {
                        prop.ByteSetter(ref instance, span, cultureLocal);
                    }
                    else
                    {
                        var decoded = FixedWidthUtf8BindingHelper.Decode(span);
                        prop.Setter(ref instance, decoded.AsSpan(), cultureLocal);
                    }
                }
                catch (Exception ex) when (ex is not FixedWidthException)
                {
                    var fieldValue = FixedWidthUtf8BindingHelper.Decode(span);
                    throw new FixedWidthException(
                        FixedWidthErrorCode.ParseError,
                        $"Failed to parse '{fieldValue}' for property '{prop.Name}': {ex.Message}",
                        row.RecordNumber,
                        row.SourceLineNumber);
                }

                if (prop.Validation is { HasAnyRule: true } validation)
                {
                    hasErrors |= PropertyValidationRunner.Validate(
                        FixedWidthUtf8BindingHelper.Decode(span),
                        prop.Name, row.RecordNumber, prop.Start,
                        columnName: null,
                        validation.NotEmpty, validation.MinLength, validation.MaxLength,
                        validation.RangeMin, validation.RangeMax, validation.Pattern,
                        errors,
                        cultureLocal);
                }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNullValue(ReadOnlySpan<byte> value, byte[][] nullValues)
    {
        foreach (var nullValue in nullValues)
        {
            if (value.SequenceEqual(nullValue))
                return true;
        }

        return false;
    }

    private static byte[][] EncodeNullValues(IReadOnlyList<string> nullValues)
    {
        var encoded = new byte[nullValues.Count][];
        for (int i = 0; i < nullValues.Count; i++)
        {
            encoded[i] = Encoding.UTF8.GetBytes(nullValues[i]);
        }

        return encoded;
    }
}
