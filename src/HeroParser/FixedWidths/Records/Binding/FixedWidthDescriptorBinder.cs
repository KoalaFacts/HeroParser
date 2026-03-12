using System.Globalization;
using System.Runtime.CompilerServices;
using HeroParser.FixedWidths.Mapping;
using HeroParser.Validation;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// High-performance binder that uses pre-compiled property descriptors.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public sealed class FixedWidthDescriptorBinder<T> : IFixedWidthBinder<T> where T : new()
{
    private readonly FixedWidthRecordDescriptor<T> descriptor;
    private readonly CultureInfo culture;
    private readonly HashSet<string>? nullValues;

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
            ? new HashSet<string>(nullValues, StringComparer.Ordinal)
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

                if (prop.Validation is { HasAnyRule: true } validation && errors is not null)
                {
                    var fieldStr = new string(span);
                    hasErrors |= PropertyValidationRunner.Validate(
                        fieldStr, prop.Name, row.RecordNumber, i,
                        columnName: prop.Name,
                        validation.NotEmpty, validation.MinLength, validation.MaxLength,
                        validation.RangeMin, validation.RangeMax, validation.Pattern,
                        errors);
                }
            }
        }
        return !hasErrors;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNullValue(ReadOnlySpan<char> value, HashSet<string> nullValues)
    {
        return nullValues.Contains(new string(value));
    }
}
