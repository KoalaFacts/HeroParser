using HeroParser.FixedWidths;
using HeroParser.Validation;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Interface for fixed-width binders that operate directly on UTF-8 byte rows.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
public interface IFixedWidthByteBinder<T> where T : new()
{
    /// <summary>
    /// Binds a fixed-width UTF-8 row to a new record instance.
    /// </summary>
    bool TryBind(FixedWidthByteSpanRow row, out T result, List<ValidationError>? errors = null);

    /// <summary>
    /// Binds a fixed-width UTF-8 row into an existing record instance.
    /// </summary>
    bool BindInto(ref T instance, FixedWidthByteSpanRow row, List<ValidationError>? errors = null);
}
