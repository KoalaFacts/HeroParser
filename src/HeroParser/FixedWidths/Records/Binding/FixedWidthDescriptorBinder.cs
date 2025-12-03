using System.Globalization;
using System.Runtime.CompilerServices;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// High-performance binder that uses pre-compiled property descriptors.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public sealed class FixedWidthDescriptorBinder<T> : IFixedWidthBinder<T> where T : class, new()
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
    public T? Bind(FixedWidthCharSpanRow row)
    {
        var instance = descriptor.Factory();
        BindInto(instance, row);
        return instance;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool BindInto(T instance, FixedWidthCharSpanRow row)
    {
        var props = descriptor.Properties;
        var cultureLocal = culture;
        var nullVals = nullValues;

        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            var field = row.GetField(prop.Start, prop.Length, prop.PadChar, prop.Alignment);
            var span = field.CharSpan;

            if (nullVals is null || !IsNullValue(span, nullVals))
            {
                try
                {
                    prop.Setter(instance, span, cultureLocal);
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
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNullValue(ReadOnlySpan<char> value, HashSet<string> nullValues)
    {
        return nullValues.Contains(new string(value));
    }
}
